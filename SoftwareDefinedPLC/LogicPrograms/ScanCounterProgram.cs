namespace SoftwareDefinedPlc.LogicPrograms
{
    /// <summary>
    /// Proof-of-life program. Every scan it increments %MD0 (a DINT scan counter, which no longer loses
    /// integer behavior beyond 2^53) and toggles output bit %QX0.1 every <see cref="HalfPeriodScans"/> scans.
    /// </summary>
    public sealed class ScanCounterProgram : ILogicProgram
    {
        public string Name => "ScanCounter";
        public ProgramLanguage Language => ProgramLanguage.ST;
        public int HalfPeriodScans { get; init; } = 25;

        public static readonly Address ScanCountAddress = Address.Parse("%MD0");
        public static readonly Address HeartbeatAddress = Address.Parse("%QX0.1");

        public void Execute(in ScanContext ctx)
        {
            int count = unchecked(ctx.Memory.ReadInt32(ScanCountAddress) + 1);
            ctx.Memory.WriteInt32(ScanCountAddress, count);
            if (count % HalfPeriodScans == 0)
                ctx.Images.Outputs.WriteBool(HeartbeatAddress, !ctx.Images.Outputs.ReadBool(HeartbeatAddress));
        }
    }
}
