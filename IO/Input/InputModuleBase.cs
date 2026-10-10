using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Input
{
    public abstract class InputModuleBase : IInputModule
    {
        public int Slot { get; }
        public abstract int ChannelCount { get; }
        public bool IsIsolated { get; init; } = true;
        public ModuleStatus Status { get; protected set; } = ModuleStatus.Ok;
        protected readonly int BaseAddress;
 
        protected InputModuleBase(int slot, int baseAddress) { Slot = slot; BaseAddress = baseAddress; }
        public abstract void ReadInto(IDataMemory inputImage);
    }
}