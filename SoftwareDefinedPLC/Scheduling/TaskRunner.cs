namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// One runner per task. A scan is: latch inputs into the task's private image, execute bound programs
    /// (each under the M/D write lock), commit outputs, write modules from the field copy, signal housekeeping
    /// (SCAN-12, MEM-7). Steady state allocates nothing (SCAN-5).
    /// </summary>
    internal sealed class TaskRunner
    {
        private readonly ILogicProgram[] _programs;
        private readonly string[] _programNames;
        private readonly IInputModule[] _inputs;
        private readonly IOutputModule[] _outputs;
        private readonly ProcessImage _image;
        private readonly DataMemory _memory;
        private readonly HousekeepingWorker? _housekeeping;
        private readonly SemaphoreSlim _eventSignal = new(0);
        private long _scanStartTs;
        private long _scanNumber;
        private volatile int _currentProgram = -1;

        public TaskRunner(TaskConfig config, ScanStats stats, ScanEnvironment env)
        {
            Config = config;
            Stats = stats;
            _memory = env.Memory;
            _image = new ProcessImage(env.Memory, env.Forces);
            _housekeeping = env.Housekeeping;
            _inputs  = env.Backplane.Modules.OfType<IInputModule>().ToArray();
            _outputs = env.Backplane.Modules.OfType<IOutputModule>().ToArray();

            _programs = new ILogicProgram[config.ProgramInstances.Count];
            _programNames = new string[_programs.Length];
            for (int i = 0; i < _programs.Length; i++)
            {
                var name = config.ProgramInstances[i];
                _programs[i] = env.Programs.Find(name)
                    ?? throw new InvalidOperationException($"Task '{config.Name}': unknown program '{name}'.");
                _programNames[i] = name;
            }

            IntervalSw = Clock.ToSw(config.Interval);
            WatchdogSw = Clock.ToSw(config.EffectiveWatchdogLimit);
        }

        public TaskConfig Config { get; }
        public string Name => Config.Name;
        public TaskKind Kind => Config.Kind;
        public int Priority => Config.Priority;
        public ScanStats Stats { get; }
        public long IntervalSw { get; }
        public long WatchdogSw { get; }

        /// <summary>Stopwatch timestamp of the scan in progress, or 0 when idle. Read by the watchdog monitor.</summary>
        public long ScanStartTs => Volatile.Read(ref _scanStartTs);
        public long ScanNumber => Volatile.Read(ref _scanNumber);
        public string? CurrentProgramName => _currentProgram is >= 0 and var i && i < _programNames.Length ? _programNames[i] : null;

        // ---- event tasks ----
        public void SignalEvent() => _eventSignal.Release();
        public bool TakePending() => _eventSignal.Wait(0);
        public bool WaitEvent(int timeoutMs) => _eventSignal.Wait(timeoutMs);
        public void Wake() => _eventSignal.Release();

        /// <returns>End-of-scan timestamp.</returns>
        public long Execute(RunSession s, long begin, TimeSpan now, TimeSpan delta, long jitterSw)
        {
            Volatile.Write(ref _scanStartTs, begin);
            try
            {
                long n = _scanNumber + 1;
                Volatile.Write(ref _scanNumber, n);
                var ctx = new ScanContext(n, now, delta, Config.Name, _image, _memory);

                _image.LatchInputs(_inputs);

                for (int i = 0; i < _programs.Length; i++)
                {
                    _currentProgram = i;
                    using (_memory.BeginExecute())          // MEM-8: held for one program execute, hold time recorded
                        _programs[i].Execute(in ctx);
                }
                _currentProgram = -1;

                // A fault while we were executing: outputs were already driven to fail-safe; never overwrite that.
                if (s.Aborted) return Clock.Now;

                _image.CommitOutputs();
                for (int i = 0; i < _outputs.Length; i++) _outputs[i].WriteFrom(_image.FieldOutputs);
                _housekeeping?.Signal();

                long end = Clock.Now;
                if (!s.Aborted) Stats.Record(Clock.ToTimeSpanTicks(end - begin), Clock.ToTimeSpanTicks(jitterSw));
                return end;
            }
            finally { Volatile.Write(ref _scanStartTs, 0); }
        }
    }
}
