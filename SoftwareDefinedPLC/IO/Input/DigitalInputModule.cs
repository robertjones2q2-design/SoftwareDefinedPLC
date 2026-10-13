namespace SoftwareDefinedPlc.IO.Input
{
    /// <summary>Channel i maps to bit (base + i) of the I image; the base must be a bit address such as %IX0.0.</summary>
    public sealed class DigitalInputModule : InputModuleBase
    {
        private readonly IDigitalSource?[] _channels;
        private readonly Address[] _addresses;
        public override int ChannelCount => _channels.Length;
        public override ImageRange ImageRange => new(MemoryArea.I, BaseAddress.BitIndex, _channels.Length);

        public DigitalInputModule(int slot, Address baseAddress, int channels) : base(slot, baseAddress)
        {
            if (baseAddress.Size != SizePrefix.X)
                throw new ArgumentException("Digital modules map to bit addresses (%IX..).", nameof(baseAddress));
            _channels = new IDigitalSource?[channels];
            _addresses = new Address[channels];
            for (int i = 0; i < channels; i++)
            {
                int bit = baseAddress.BitIndex + i;
                _addresses[i] = new Address(MemoryArea.I, SizePrefix.X, bit >> 3, bit & 7);
            }
        }

        /// <summary>Spec 1 convenience: baseAddress is a bit index into the I image.</summary>
        public DigitalInputModule(int slot, int baseAddress, int channels)
            : this(slot, new Address(MemoryArea.I, SizePrefix.X, baseAddress >> 3, baseAddress & 7), channels) { }

        public void Wire(int channel, IDigitalSource source) => _channels[channel] = source;

        public override void ReadInto(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                image.WriteBool(_addresses[i], _channels[i]?.Read() ?? false);
        }
    }
}
