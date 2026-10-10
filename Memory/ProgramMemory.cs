using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftwareDefinedPlc.Memory
{
    public sealed class ProgramMemory : IProgramMemory
    {
        public ILogicProgram? Current { get; private set; }
        public void Store(ILogicProgram program) => Current = program;
    }
}