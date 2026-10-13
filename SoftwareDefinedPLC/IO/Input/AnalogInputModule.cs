namespace SoftwareDefinedPlc.IO.Input
{
    /// <summary>Channel i is a 32-bit REAL at byte (base + 4 * i) of the I image; the base must be a %ID address.</summary>
    public sealed class AnalogInputModule : InputModuleBase
    {
        private readonly IAnalogSource?[] _channels;
        private readonly Address[] _addresses;
        public int ResolutionBits { get; init; } = 12;    // ADC resolution
        public override int ChannelCount => _channels.Length;
        public override ImageRange ImageRange => new(MemoryArea.I, BaseAddress.ByteOffset * 8, _channels.Length * 32);

        public AnalogInputModule(int slot, Address baseAddress, int channels) : base(slot, baseAddress)
        {
            if (baseAddress.Size != SizePrefix.D)
                throw new ArgumentException("Analog modules map 32-bit REAL channels (%ID..).", nameof(baseAddress));
            _channels = new IAnalogSource?[channels];
            _addresses = new Address[channels];
            for (int i = 0; i < channels; i++)
                _addresses[i] = new Address(MemoryArea.I, SizePrefix.D, baseAddress.ByteOffset + 4 * i);
        }

        public void Wire(int channel, IAnalogSource source) => _channels[channel] = source;

        public override void ReadInto(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                image.WriteSingle(_addresses[i], (float)(_channels[i]?.Read() ?? 0.0));
        }
    }
}
