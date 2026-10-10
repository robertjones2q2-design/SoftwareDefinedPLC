using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Memory
{
    public sealed class DataMemory : IDataMemory
    {
        private readonly Dictionary<int, bool>   _bits  = new();
        private readonly Dictionary<int, double> _words = new();
 
        public bool ReadBit(Address a) => _bits.TryGetValue(a.Index, out var v) && v;
        public void WriteBit(Address a, bool value) => _bits[a.Index] = value;
        public double ReadWord(Address a) => _words.TryGetValue(a.Index, out var v) ? v : 0.0;
        public void WriteWord(Address a, double value) => _words[a.Index] = value;
    }
}