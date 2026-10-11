namespace SoftwareDefinedPlc.Memory
{
    public sealed class ProgramMemory : IProgramMemory
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ILogicProgram> _programs = new(StringComparer.Ordinal);

        public int Count { get { lock (_gate) return _programs.Count; } }
        public void Store(ILogicProgram program) { lock (_gate) _programs[program.Name] = program; }
        public ILogicProgram? Find(string name) { lock (_gate) return _programs.TryGetValue(name, out var p) ? p : null; }
        public void Clear() { lock (_gate) _programs.Clear(); }
    }
}
