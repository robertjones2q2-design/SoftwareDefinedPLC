using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Output
{
    public abstract class OutputModuleBase : IOutputModule
    {
        public int Slot { get; }
        public abstract int ChannelCount { get; }
        public bool IsIsolated { get; init; } = true;
        public ModuleStatus Status { get; protected set; } = ModuleStatus.Ok;
        protected readonly int BaseAddress;
 
        protected OutputModuleBase(int slot, int baseAddress) { Slot = slot; BaseAddress = baseAddress; }
        public abstract void WriteFrom(IDataMemory image);
    }
}