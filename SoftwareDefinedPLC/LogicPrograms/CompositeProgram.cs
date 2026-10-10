namespace SoftwareDefinedPlc.LogicPrograms
{
    /// <summary>Runs several programs in order, each scan (ProgramMemory holds only one).</summary>
    public sealed class CompositeProgram : ILogicProgram
    {
        private readonly ILogicProgram[] _parts;
        public CompositeProgram(string name, params ILogicProgram[] parts) { Name = name; _parts = parts; }
        public string Name { get; }
        public ProgramLanguage Language => ProgramLanguage.ST;

        public void Execute(IDataMemory i, IDataMemory q, IDataMemory m)
        {
            foreach (var p in _parts) p.Execute(i, q, m);
        }
    }
}
