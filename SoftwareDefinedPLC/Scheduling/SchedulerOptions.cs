namespace SoftwareDefinedPlc.Scheduling
{
    public sealed class SchedulerOptions
    {
        /// <summary>
        /// No threads, no wall clock: <c>Plc.Tick()</c> runs one scan of every task in priority
        /// order with synthetic time (Delta = Interval). For unit tests only (SCAN-1).
        /// </summary>
        public bool Deterministic { get; init; }

        public IThreadTuner ThreadTuner { get; init; } = ThreadTuners.CreateDefault();
        public TaskTuning? DefaultTuning { get; init; }
        public IReadOnlyDictionary<string, TaskTuning> Tuning { get; init; } = new Dictionary<string, TaskTuning>();

        /// <summary>Sleep until this long before the deadline, then spin (SCAN-3).</summary>
        public TimeSpan SpinThreshold { get; init; } = TimeSpan.FromMilliseconds(1);
        public TimeSpan WatchdogPollInterval { get; init; } = TimeSpan.FromMilliseconds(2);
        public TimeSpan ThreadJoinTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

        /// <summary>SCAN-5: GCSettings.LatencyMode = SustainedLowLatency while in Run.</summary>
        public bool UseSustainedLowLatencyGc { get; init; } = true;

        public Action<string>? Log { get; init; }
    }
}
