namespace SoftwareDefinedPlc.LogicPrograms
{
    /// <summary>Start/stop motor seal-in: %QX0.0 = (%IX0.0 OR %QX0.0) AND NOT %IX0.1</summary>
    public sealed class MotorStartStop : ILogicProgram
    {
        private static readonly Address StartBit = Address.Parse("%IX0.0");
        private static readonly Address StopBit  = Address.Parse("%IX0.1");
        private static readonly Address MotorBit = Address.Parse("%QX0.0");

        public string Name => "MotorStartStop";
        public ProgramLanguage Language => ProgramLanguage.ST;

        public void Execute(in ScanContext ctx)
        {
            bool start = ctx.Images.Inputs.ReadBool(StartBit);
            bool stop  = ctx.Images.Inputs.ReadBool(StopBit);
            bool motor = ctx.Images.Outputs.ReadBool(MotorBit);
            ctx.Images.Outputs.WriteBool(MotorBit, (start || motor) && !stop);
        }
    }
}
