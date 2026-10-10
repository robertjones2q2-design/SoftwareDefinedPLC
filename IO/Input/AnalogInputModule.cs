using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Input
{
    public sealed class AnalogInputModule : InputModuleBase
    {
        private readonly IAnalogSource?[] _channels;
        public int ResolutionBits { get; init; } = 12;    // ADC resolution
        public override int ChannelCount => _channels.Length;
 
        public AnalogInputModule(int slot, int baseAddress, int channels)
            : base(slot, baseAddress) => _channels = new IAnalogSource?[channels];
 
        public void Wire(int channel, IAnalogSource source) => _channels[channel] = source;
 
        public override void ReadInto(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                image.WriteWord(new Address(BaseAddress + i), _channels[i]?.Read() ?? 0.0);
        }
    }
}