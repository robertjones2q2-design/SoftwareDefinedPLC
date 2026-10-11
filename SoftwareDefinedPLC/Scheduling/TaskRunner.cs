namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// Replaces ScanCycle: one runner per task. A scan is read inputs, execute bound programs,
    /// write outputs, signal housekeeping (SCAN-12). Steady state allocates nothing (SCAN-5):
    /// module and program lists are resolved to arrays once, when the runner is built.
    /// </summary>
    internal sealed class TaskRunner
    {
        private readonly ILogicProgram[] _programs;
        private readonly string[] _programNames;
        private readonly IInputModule[] _inputs;
        private readonly IOutputModule[] _outputs;
        private readonly IProcessImage _images;
        private readonly IDataMemory _memory;
        private readonly HousekeepingWorker? _housekeeping;
        private readonly SemaphoreSlim _eventSignal = new(0);
        private long _scanStartTs;
        private long _scanNumber;
        private volatile int _currentProgram = -1;

        public TaskRunner(TaskConfig config, ScanStats stats, ScanEnvironment env)
        {
            Config = config;
            Stats = stats;
            _images = env.Images;
            _memory = env.Memory;
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
                var ctx = new ScanContext(n, now, delta, Config.Name, _images, _memory);

                for (int i = 0; i < _inputs.Length; i++) _inputs[i].ReadInto(_images.Inputs);

                for (int i = 0; i < _programs.Length; i++)
                {
                    _currentProgram = i;
                    _programs[i].Execute(in ctx);
                }
                _currentProgram = -1;

                // A fault while we were executing: outputs were already driven to fail-safe; never overwrite that.
                if (s.Aborted) return Clock.Now;

                for (int i = 0; i < _outputs.Length; i++) _outputs[i].WriteFrom(_images.Outputs);
                _housekeeping?.Signal();

                long end = Clock.Now;
                if (!s.Aborted) Stats.Record(Clock.ToTimeSpanTicks(end - begin), Clock.ToTimeSpanTicks(jitterSw));
                return end;
            }
            finally { Volatile.Write(ref _scanStartTs, 0); }
        }
    }
}
