using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftwareDefinedPlc.Cpu
{
    public interface IScanCycle
    {
        TimeSpan LastScanTime { get; }
        void RunOnce();
    }
}