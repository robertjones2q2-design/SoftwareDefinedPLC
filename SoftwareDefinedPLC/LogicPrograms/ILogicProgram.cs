namespace SoftwareDefinedPlc.LogicPrograms
{
    public interface ILogicProgram
    {
        string Name { get; }
        ProgramLanguage Language { get; }
        void Execute(in ScanContext ctx);
    }
}
