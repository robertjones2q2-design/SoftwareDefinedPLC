namespace SoftwareDefinedPlc.LogicPrograms
{
    /// <summary>
    /// Proof-of-life program. Every scan it increments M word 0 (scan counter)
    /// and toggles output bit Q1 every <see cref="HalfPeriodScans"/> scans (heartbeat).
    /// </summary>
    public sealed class ScanCounterProgram : ILogicProgram
    {
        public string Name => "ScanCounter";
        public ProgramLanguage Language => ProgramLanguage.ST;
        public int HalfPeriodScans { get; init; } = 25;

        public static readonly Address ScanCountAddress = new(0);
        public static readonly Address HeartbeatAddress = new(1);

        public void Execute(in ScanContext ctx)
        {
            double count = ctx.Memory.ReadWord(ScanCountAddress) + 1;
            ctx.Memory.WriteWord(ScanCountAddress, count);
            if (count % HalfPeriodScans == 0)
                ctx.Images.Outputs.WriteBit(HeartbeatAddress, !ctx.Images.Outputs.ReadBit(HeartbeatAddress));
        }
    }
}
