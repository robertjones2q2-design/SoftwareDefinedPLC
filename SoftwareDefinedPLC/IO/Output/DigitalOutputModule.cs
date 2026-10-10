using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Output
{
    public sealed class DigitalOutputModule : OutputModuleBase
    {
        private readonly IDigitalSink?[] _channels;
        public OutputType Type { get; init; } = OutputType.Transistor;
        public override int ChannelCount => _channels.Length;
 
        public DigitalOutputModule(int slot, int baseAddress, int channels)
            : base(slot, baseAddress) => _channels = new IDigitalSink?[channels];
 
        public void Wire(int channel, IDigitalSink sink) => _channels[channel] = sink;
 
        public override void WriteFrom(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i]?.Write(image.ReadBit(new Address(BaseAddress + i)));
        }
    }
}