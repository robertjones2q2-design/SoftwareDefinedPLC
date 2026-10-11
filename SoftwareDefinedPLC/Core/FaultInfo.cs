namespace SoftwareDefinedPlc.Core
{
    /// <summary>Spec 7 (PER-12) extends this list and renames the enum to FaultCode.</summary>
    public enum FaultReason
    {
        None,
        WatchdogTimeout,
        ProgramRuntimeError,
        IoModuleFault,
        PowerLoss,
        ConfigurationInvalid,
        InternalError,
    }

    public sealed record FaultInfo(
        FaultReason Reason, string Message, string? Source,
        DateTimeOffset Time, long ScanNumber, bool Recoverable);

    public sealed class InvalidModeTransitionException : InvalidOperationException
    {
        public CpuMode From { get; }
        public CpuMode To { get; }
        public InvalidModeTransitionException(CpuMode from, CpuMode to)
            : base($"Illegal CPU mode transition {from} -> {to}.") { From = from; To = to; }
    }
}
