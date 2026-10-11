namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>Everything a task needs to scan; owned by the Cpu and handed to the scheduler.</summary>
    public sealed class ScanEnvironment
    {
        public required IProgramMemory Programs { get; init; }
        public required IBackplane Backplane { get; init; }
        public required IProcessImage Images { get; init; }
        public required IDataMemory Memory { get; init; }
        public HousekeepingWorker? Housekeeping { get; init; }
    }
}
