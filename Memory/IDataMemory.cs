using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftwareDefinedPlc.Memory
{
    public interface IDataMemory
    {
        bool   ReadBit(Address a);
        void   WriteBit(Address a, bool value);
        double ReadWord(Address a);
        void   WriteWord(Address a, double value);
    }
}