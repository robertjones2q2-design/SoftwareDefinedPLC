namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// SCAN-1/3/4/7/8/9/11. One dedicated thread per task, absolute-deadline timing, a watchdog
    /// monitor on its own thread, and a priority-ordered cooperative scan gate.
    /// (Named PlcTaskScheduler to avoid clashing with System.Threading.Tasks.TaskScheduler.)
    /// </summary>
    public sealed class PlcTaskScheduler : ITaskScheduler
    {
        private readonly ScanEnvironment _env;
        private readonly SchedulerOptions _opt;
        private readonly object _life = new();
        private List<TaskConfig> _configs = new();
        private volatile IReadOnlyDictionary<string, ScanStats> _stats = new Dictionary<string, ScanStats>();
        private RunSession? _session;

        public PlcTaskScheduler(ScanEnvironment env, SchedulerOptions? options = null)
        { _env = env; _opt = options ?? new SchedulerOptions(); }

        public event Action<FaultInfo>? Faulted;

        public IReadOnlyDictionary<string, ScanStats> Stats => _stats;
        public IReadOnlyList<TaskConfig> Tasks { get { lock (_life) return _configs.ToArray(); } }
        public bool IsRunning => _session is { Active: true };
        /// <summary>Threads that did not exit within the join timeout (e.g. a hung program). They cannot touch outputs again.</summary>
        public int AbandonedThreads { get; private set; }

        // ------------------------------------------------------------ configuration

        public void Configure(IReadOnlyList<TaskConfig> tasks)
        {
            lock (_life)
            {
                if (_session is { Active: true }) throw new InvalidOperationException("Stop the CPU before changing the task configuration.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in tasks)
                {
                    t.Validate();
                    if (!names.Add(t.Name)) throw new ArgumentException($"Duplicate task name '{t.Name}'.");
                }
                var old = _stats;
                var fresh = new Dictionary<string, ScanStats>(StringComparer.Ordinal);
                foreach (var t in tasks) fresh[t.Name] = old.TryGetValue(t.Name, out var s) ? s : new ScanStats();
                _configs = tasks.ToList();
                _stats = fresh;
            }
        }

        /// <summary>Problems that would stop Run from starting (unknown program names).</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            lock (_life)
                foreach (var c in _configs)
                    foreach (var p in c.ProgramInstances)
                        if (_env.Programs.Find(p) is null)
                            errors.Add($"Task '{c.Name}' references unknown program '{p}'.");
            return errors;
        }

        public void ResetStats(string? taskName = null)
        {
            foreach (var (name, s) in _stats)
                if (taskName is null || name == taskName) s.Reset();
        }

        // ------------------------------------------------------------ lifecycle

        public void Start()
        {
            lock (_life)
            {
                if (_session is { Active: true }) throw new InvalidOperationException("Scheduler already running.");
                StopCore();   // join leftovers of a faulted session

                var errors = Validate();
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

                var runners = _configs
                    .Select(c => new TaskRunner(c, _stats[c.Name], _env))
                    .OrderBy(r => r.Priority)            // stable: ties keep configuration order
                    .ToArray();
                var s = new RunSession(runners);
                _session = s;

                if (_opt.Deterministic) { RunStartupTasks(s); return; }

                PlatformTimer.Begin();
                StartThread(s, "plc-watchdog", () => MonitorLoop(s), ThreadPriority.AboveNormal);
                RunStartupTasks(s);                       // SCAN: startup tasks run once, before cyclic ones
                if (!s.Active) return;                    // faulted during startup

                foreach (var r in runners)
                {
                    if (r.Kind == TaskKind.Startup) continue;
                    var runner = r;
                    StartThread(s, $"plc-task-{r.Name}",
                        () => { ApplyTuning(runner); if (runner.Kind == TaskKind.Cyclic) CyclicLoop(s, runner); else EventLoop(s, runner); },
                        ThreadPriority.Highest);
                }
            }
        }

        public void Stop() { lock (_life) StopCore(); }

        /// <summary>Non-blocking: stop all tasks right now and refuse further output writes. Used for faults.</summary>
        public void Abort() => _session?.TryAbort();

        private void StopCore()
        {
            var s = _session;
            if (s is null) return;
            s.Deactivate();
            foreach (var r in s.Runners) r.Wake();

            var deadline = DateTime.UtcNow + _opt.ThreadJoinTimeout;
            foreach (var t in s.Threads)
            {
                if (t == Thread.CurrentThread) continue;
                var left = deadline - DateTime.UtcNow;
                if (!t.Join(left > TimeSpan.Zero ? left : TimeSpan.Zero))
                {
                    AbandonedThreads++;
                    // From here on the session is dead: the thread cannot write outputs or stats.
                    s.TryAbort();
                    _opt.Log?.Invoke($"Thread '{t.Name}' did not stop within {_opt.ThreadJoinTimeout.TotalMilliseconds:F0} ms and was abandoned.");
                }
            }
            PlatformTimer.End();
            _session = null;
        }

        public void Trigger(string taskName)
        {
            var s = _session;
            var r = s?.Runners.FirstOrDefault(x => x.Name == taskName && x.Kind == TaskKind.Event);
            if (s is { Active: true } && r is not null) r.SignalEvent();
            else throw new InvalidOperationException($"No running event task '{taskName}'.");
        }

        // ------------------------------------------------------------ deterministic mode

        /// <summary>One scan of every non-startup task, in priority order, with synthetic time. Allocation-free.</summary>
        public void Tick()
        {
            if (!_opt.Deterministic)
                throw new InvalidOperationException("Tick() is for deterministic unit tests only; production hosts use the task scheduler (SCAN-1).");
            var s = _session;
            if (s is not { Active: true }) return;

            var runners = s.Runners;
            for (int i = 0; i < runners.Length; i++)
            {
                var r = runners[i];
                if (!s.Active) break;
                if (r.Kind == TaskKind.Startup) continue;
                if (r.Kind == TaskKind.Event && !r.TakePending()) continue;

                var interval = r.Config.Interval;
                var now = TimeSpan.FromTicks(interval.Ticks * (r.ScanNumber + 1));
                try { r.Execute(s, Clock.Now, now, interval, 0); }
                catch (Exception ex) { ReportProgramFault(s, r, ex); break; }
            }
        }

        // ------------------------------------------------------------ task loops

        private void StartThread(RunSession s, string name, Action body, ThreadPriority priority)
        {
            var t = new Thread(() => body()) { IsBackground = true, Name = name, Priority = priority };
            s.Threads.Add(t);
            t.Start();
        }

        private void ApplyTuning(TaskRunner r)
        {
            var tuning = _opt.Tuning.TryGetValue(r.Name, out var t) ? t : _opt.DefaultTuning;
            if (tuning is null) return;
            var warning = _opt.ThreadTuner.TuneCurrentThread(r.Name, tuning);
            if (warning is not null) _opt.Log?.Invoke(warning);
        }

        private void RunStartupTasks(RunSession s)
        {
            foreach (var r in s.Runners)
            {
                if (r.Kind != TaskKind.Startup) continue;
                if (!s.Active) return;
                long b = Clock.Now;
                try { r.Execute(s, b, _opt.Deterministic ? TimeSpan.Zero : Clock.ToTimeSpan(b - s.StartTs), TimeSpan.Zero, 0); }
                catch (Exception ex) { ReportProgramFault(s, r, ex); return; }
            }
        }

        private void CyclicLoop(RunSession s, TaskRunner r)
        {
            long interval = r.IntervalSw;
            long start = Clock.Now;
            long n = 1, prev = 0;               // deadline(n) = start + n * interval  (SCAN-3)

            while (s.Active)
            {
                long deadline = start + n * interval;
                if (!WaitUntil(s, deadline)) break;
                if (!ScanOnce(s, r, ref prev, deadline, out long end)) break;
                if (!s.Active) break;

                long next = start + (n + 1) * interval;
                if (end > next)
                {
                    // SCAN-9: overrun. Count it and skip the missed periods instead of bursting to catch up.
                    r.Stats.RecordOverrun();
                    n = (end - start) / interval + 1;
                }
                else n++;
            }
        }

        private void EventLoop(RunSession s, TaskRunner r)
        {
            long prev = 0;
            while (s.Active)
            {
                if (!r.WaitEvent(50)) continue;
                if (!s.Active) break;
                if (!ScanOnce(s, r, ref prev, 0, out _)) break;
            }
        }

        private bool ScanOnce(RunSession s, TaskRunner r, ref long prevStart, long deadline, out long end)
        {
            end = 0;
            if (!s.Gate.Enter(r.Priority)) return false;
            try
            {
                if (!s.Active) return false;
                long begin = Clock.Now;
                var delta = prevStart == 0 ? r.Config.Interval : Clock.ToTimeSpan(begin - prevStart);
                prevStart = begin;
                long jitter = deadline == 0 ? 0 : Math.Max(0, begin - deadline);
                end = r.Execute(s, begin, Clock.ToTimeSpan(begin - s.StartTs), delta, jitter);
                return true;
            }
            catch (Exception ex) { ReportProgramFault(s, r, ex); return false; }
            finally { s.Gate.Exit(); }
        }

        /// <summary>Sleep until ~SpinThreshold before the deadline, then spin on the Stopwatch (SCAN-3).</summary>
        private bool WaitUntil(RunSession s, long deadline)
        {
            long spin = Clock.ToSw(_opt.SpinThreshold);
            while (true)
            {
                long remaining = deadline - Clock.Now;
                if (remaining <= 0) return s.Active;
                if (!s.Active) return false;
                long sleepSw = remaining - spin;
                if (sleepSw >= Clock.PerMillisecond)
                {
                    if (s.StopSignal.Wait((int)(sleepSw / Clock.PerMillisecond))) return false;
                }
                else Thread.SpinWait(8);
            }
        }

        // ------------------------------------------------------------ watchdog (SCAN-7/8)

        private void MonitorLoop(RunSession s)
        {
            int poll = Math.Max(1, (int)_opt.WatchdogPollInterval.TotalMilliseconds);
            while (!s.StopSignal.Wait(poll))
            {
                long now = Clock.Now;
                foreach (var r in s.Runners)
                {
                    long st = r.ScanStartTs;
                    if (st == 0 || now - st <= r.WatchdogSw || r.ScanStartTs != st) continue;

                    if (!s.TryAbort()) return;
                    var ran = Clock.ToTimeSpan(now - st);
                    RaiseFaulted(new FaultInfo(FaultReason.WatchdogTimeout,
                        $"Task '{r.Name}' ran {ran.TotalMilliseconds:F1} ms, limit {r.Config.EffectiveWatchdogLimit.TotalMilliseconds:F1} ms" +
                        (r.CurrentProgramName is { } p ? $" (in program '{p}')." : "."),
                        r.Name, DateTimeOffset.UtcNow, r.ScanNumber, Recoverable: true));
                    return;
                }
            }
        }

        private void ReportProgramFault(RunSession s, TaskRunner r, Exception ex)
        {
            if (!s.TryAbort()) return;
            RaiseFaulted(new FaultInfo(FaultReason.ProgramRuntimeError,
                $"{ex.GetType().Name}: {ex.Message}",
                r.CurrentProgramName is { } p ? $"{r.Name}/{p}" : r.Name,
                DateTimeOffset.UtcNow, r.ScanNumber, Recoverable: true));
        }

        private void RaiseFaulted(FaultInfo info)
        {
            try { Faulted?.Invoke(info); }
            catch (Exception ex) { _opt.Log?.Invoke($"Faulted handler threw: {ex.Message}"); }
        }
    }
}
