namespace SoftwareDefinedPlc.Memory
{
    /// <summary>
    /// Dictionary-backed memory. A plain lock makes it safe now that scan threads,
    /// the housekeeping worker and monitoring callers touch it concurrently.
    /// Spec 2 replaces this with the typed, area-based model (MEM-8).
    /// </summary>
    public sealed class DataMemory : IDataMemory
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, bool>   _bits  = new();
        private readonly Dictionary<int, double> _words = new();

        public bool ReadBit(Address a) { lock (_gate) return _bits.TryGetValue(a.Index, out var v) && v; }
        public void WriteBit(Address a, bool value) { lock (_gate) _bits[a.Index] = value; }
        public double ReadWord(Address a) { lock (_gate) return _words.TryGetValue(a.Index, out var v) ? v : 0.0; }
        public void WriteWord(Address a, double value) { lock (_gate) _words[a.Index] = value; }
    }
}
