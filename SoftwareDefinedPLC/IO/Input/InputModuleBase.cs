namespace SoftwareDefinedPlc.IO.Input
{
    public abstract class InputModuleBase : IInputModule
    {
        public int Slot { get; }
        public abstract int ChannelCount { get; }
        public abstract ImageRange ImageRange { get; }
        public bool IsIsolated { get; init; } = true;
        public ModuleStatus Status { get; protected set; } = ModuleStatus.Ok;
        protected readonly Address BaseAddress;

        protected InputModuleBase(int slot, Address baseAddress)
        {
            if (baseAddress.Area != MemoryArea.I)
                throw new ArgumentException($"Input module in slot {slot} must map into the I area, not {baseAddress.Area}.", nameof(baseAddress));
            Slot = slot; BaseAddress = baseAddress;
        }
        public abstract void ReadInto(IDataMemory inputImage);
    }
}
