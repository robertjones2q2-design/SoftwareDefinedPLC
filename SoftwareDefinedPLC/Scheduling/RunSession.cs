namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// One Run period. A fresh session per Run means a thread stuck in a previous run (e.g. a hung
    /// program) can never come back to life and touch outputs or stats of the next one.
    /// </summary>
    internal sealed class RunSession
    {
        private int _aborted;
        public RunSession(TaskRunner[] runners) { Runners = runners; StartTs = Clock.Now; }

        public TaskRunner[] Runners { get; }
        public long StartTs { get; }
        public ScanGate Gate { get; } = new();
        public ManualResetEventSlim StopSignal { get; } = new(false);
        public List<Thread> Threads { get; } = new();

        /// <summary>Task loops keep going while true.</summary>
        public volatile bool Active = true;

        /// <summary>True after a fault/abort: in-flight scans must not write outputs or stats.</summary>
        public bool Aborted => Volatile.Read(ref _aborted) != 0;

        /// <summary>Orderly stop: in-flight scans complete.</summary>
        public void Deactivate() { Active = false; StopSignal.Set(); }

        /// <returns>true only for the first caller, so a fault is reported exactly once.</returns>
        public bool TryAbort()
        {
            bool first = Interlocked.Exchange(ref _aborted, 1) == 0;
            Active = false;
            StopSignal.Set();
            Gate.Abort();
            return first;
        }
    }
}
