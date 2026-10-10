using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Input
{
    public sealed class DigitalInputModule : InputModuleBase
    {
        private readonly IDigitalSource?[] _channels;
        public override int ChannelCount => _channels.Length;
 
        public DigitalInputModule(int slot, int baseAddress, int channels)
            : base(slot, baseAddress) => _channels = new IDigitalSource?[channels];
 
        public void Wire(int channel, IDigitalSource source) => _channels[channel] = source;
 
        public override void ReadInto(IDataMemory image)
        {
            for (int i = 0; i < _channels.Length; i++)
                image.WriteBit(new Address(BaseAddress + i), _channels[i]?.Read() ?? false);
        }
    }
}