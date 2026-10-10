using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO
{
    public interface IIoModule
    {
        int Slot { get; }
        int ChannelCount { get; }
        bool IsIsolated { get; }          // optocoupler / transformer isolation
        ModuleStatus Status { get; }
    }
}