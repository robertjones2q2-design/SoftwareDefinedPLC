namespace SoftwareDefinedPlc.LogicPrograms
{
    /// <summary>Start/stop motor seal-in: Q0 = (I0 OR Q0) AND NOT I1</summary>
    public sealed class MotorStartStop : ILogicProgram
    {
        public string Name => "MotorStartStop";
        public ProgramLanguage Language => ProgramLanguage.ST;

        public void Execute(in ScanContext ctx)
        {
            bool start = ctx.Images.Inputs.ReadBit(new Address(0));
            bool stop  = ctx.Images.Inputs.ReadBit(new Address(1));
            bool motor = ctx.Images.Outputs.ReadBit(new Address(0));
            ctx.Images.Outputs.WriteBit(new Address(0), (start || motor) && !stop);
        }
    }
}
