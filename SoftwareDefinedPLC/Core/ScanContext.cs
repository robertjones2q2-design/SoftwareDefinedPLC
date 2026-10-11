namespace SoftwareDefinedPlc.Core
{
    /// <summary>Passed by <c>in</c> to every program execution (SCAN-6).</summary>
    /// <param name="ScanNumber">1-based scan count of this task since entering Run.</param>
    /// <param name="Now">Monotonic time since Run was entered.</param>
    /// <param name="Delta">Time since the previous scan of this task started (nominal interval on the first scan).</param>
    public readonly record struct ScanContext(
        long ScanNumber, TimeSpan Now, TimeSpan Delta, string TaskName,
        IProcessImage Images, IDataMemory Memory);
}
