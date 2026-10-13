namespace SoftwareDefinedPlc.IO.Output
{
    /// <summary>Channel i maps to bit (base + i) of the Q image; the base must be a bit address such as %QX0.0.</summary>
    public sealed class DigitalOutputModule : OutputModuleBase
    {
        private readonly IDigitalSink?[] _channels;
        private readonly Address[] _addresses;
        public OutputType Type { get; init; } = OutputType.Transistor;
        public override int ChannelCount => _channels.Length;
        public override ImageRange ImageRange => new(MemoryArea.Q, BaseAddress.BitIndex, _channels.Length);

        public DigitalOutputModule(int slot, Address baseAddress, int channels) : base(slot, baseAddress)
        {
            if (baseAddress.Size != SizePrefix.X)
                throw new ArgumentException("Digital modules map to bit addresses (%QX..).", nameof(baseAddress));
            _channels = new IDigitalSink?[channels];
            _addresses = new Address[channels];
            for (int i = 0; i < channels; i++)
            {
                int bit = baseAddress.BitIndex + i;
                _addresses[i] = new Address(MemoryArea.Q, SizePrefix.X, bit >> 3, bit & 7);
            }
        }

        /// <summary>Spec 1 convenience: baseAddress is a bit index into the Q image.</summary>
        public DigitalOutputModule(int slot, int baseAddress, int channels)
            : this(slot, new Address(MemoryArea.Q, SizePrefix.X, baseAddress >> 3, baseAddress & 7), channels) { }

        public void Wire(int channel, IDigitalSink sink) => _channels[channel] = sink;

        public override void ApplyFailSafe()
        {
            for (int i = 0; i < _channels.Length; i++) _channels[i]?.Write(false);
        }

        public override void WriteFrom(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i]?.Write(image.ReadBool(_addresses[i]));
        }
    }
}
