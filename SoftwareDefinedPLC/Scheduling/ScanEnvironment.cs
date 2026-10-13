namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>Everything a task needs to scan; owned by the Cpu and handed to the scheduler.</summary>
    public sealed class ScanEnvironment
    {
        public required IProgramMemory Programs { get; init; }
        public required IBackplane Backplane { get; init; }
        /// <summary>Shared memory (areas I, Q, M, D). Each task builds its own <see cref="ProcessImage"/> on top of it.</summary>
        public required DataMemory Memory { get; init; }
        public required ForceTable Forces { get; init; }
        public HousekeepingWorker? Housekeeping { get; init; }
    }
}
