namespace SoftwareDefinedPlc.Scheduling
{
    public enum TaskKind { Cyclic, Event, Startup }

    /// <summary>
    /// SCAN-2. Priority 0..31, lower runs first. A zero <see cref="WatchdogLimit"/> means
    /// "use the default": 3 x Interval, or 1 s for tasks without an interval (SCAN-7).
    /// </summary>
    public sealed record TaskConfig(
        string Name, TaskKind Kind, TimeSpan Interval,
        int Priority, TimeSpan WatchdogLimit,
        IReadOnlyList<string> ProgramInstances)
    {
        public const int MaxPriority = 31;
        public static readonly TimeSpan DefaultNoIntervalWatchdog = TimeSpan.FromSeconds(1);

        public TimeSpan EffectiveWatchdogLimit =>
            WatchdogLimit > TimeSpan.Zero ? WatchdogLimit
            : Interval > TimeSpan.Zero ? Interval * 3
            : DefaultNoIntervalWatchdog;

        public static TaskConfig Cyclic(string name, TimeSpan interval, int priority, params string[] programs) =>
            new(name, TaskKind.Cyclic, interval, priority, TimeSpan.Zero, programs);

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Task name is required.");
            if (Priority < 0 || Priority > MaxPriority)
                throw new ArgumentException($"Task '{Name}': priority {Priority} outside 0..{MaxPriority}.");
            if (Kind == TaskKind.Cyclic && Interval <= TimeSpan.Zero)
                throw new ArgumentException($"Task '{Name}': cyclic tasks need a positive interval.");
            if (WatchdogLimit < TimeSpan.Zero)
                throw new ArgumentException($"Task '{Name}': negative watchdog limit.");
            if (ProgramInstances is null) throw new ArgumentException($"Task '{Name}': ProgramInstances is null.");
        }
    }
}
