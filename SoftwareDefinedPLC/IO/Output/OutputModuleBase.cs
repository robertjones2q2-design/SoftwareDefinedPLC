namespace SoftwareDefinedPlc.IO.Output
{
    public abstract class OutputModuleBase : IOutputModule
    {
        public int Slot { get; }
        public abstract int ChannelCount { get; }
        public abstract ImageRange ImageRange { get; }
        public bool IsIsolated { get; init; } = true;
        public ModuleStatus Status { get; protected set; } = ModuleStatus.Ok;
        protected readonly Address BaseAddress;

        protected OutputModuleBase(int slot, Address baseAddress)
        {
            if (baseAddress.Area != MemoryArea.Q)
                throw new ArgumentException($"Output module in slot {slot} must map into the Q area, not {baseAddress.Area}.", nameof(baseAddress));
            Slot = slot; BaseAddress = baseAddress;
        }
        public abstract void WriteFrom(IDataMemory image);
        public abstract void ApplyFailSafe();
    }
}
