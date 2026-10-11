using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Output
{
    public sealed class AnalogOutputModule : OutputModuleBase
    {
        private readonly IAnalogSink?[] _channels;
        public int ResolutionBits { get; init; } = 12;    // DAC resolution
        public override int ChannelCount => _channels.Length;
 
        public AnalogOutputModule(int slot, int baseAddress, int channels)
            : base(slot, baseAddress) => _channels = new IAnalogSink?[channels];
 
        public void Wire(int channel, IAnalogSink sink) => _channels[channel] = sink;
 
        public override void ApplyFailSafe()
        {
            for (int i = 0; i < _channels.Length; i++) _channels[i]?.Write(0.0);
        }

        public override void WriteFrom(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i]?.Write(image.ReadWord(new Address(BaseAddress + i)));
        }
    }
}