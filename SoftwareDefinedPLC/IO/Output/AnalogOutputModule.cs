namespace SoftwareDefinedPlc.IO.Output
{
    /// <summary>Channel i is a 32-bit REAL at byte (base + 4 * i) of the Q image; the base must be a %QD address.</summary>
    public sealed class AnalogOutputModule : OutputModuleBase
    {
        private readonly IAnalogSink?[] _channels;
        private readonly Address[] _addresses;
        public int ResolutionBits { get; init; } = 12;    // DAC resolution
        public override int ChannelCount => _channels.Length;
        public override ImageRange ImageRange => new(MemoryArea.Q, BaseAddress.ByteOffset * 8, _channels.Length * 32);

        public AnalogOutputModule(int slot, Address baseAddress, int channels) : base(slot, baseAddress)
        {
            if (baseAddress.Size != SizePrefix.D)
                throw new ArgumentException("Analog modules map 32-bit REAL channels (%QD..).", nameof(baseAddress));
            _channels = new IAnalogSink?[channels];
            _addresses = new Address[channels];
            for (int i = 0; i < channels; i++)
                _addresses[i] = new Address(MemoryArea.Q, SizePrefix.D, baseAddress.ByteOffset + 4 * i);
        }

        public void Wire(int channel, IAnalogSink sink) => _channels[channel] = sink;

        public override void ApplyFailSafe()
        {
            for (int i = 0; i < _channels.Length; i++) _channels[i]?.Write(0.0);
        }

        public override void WriteFrom(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i]?.Write(image.ReadSingle(_addresses[i]));
        }
    }
}
