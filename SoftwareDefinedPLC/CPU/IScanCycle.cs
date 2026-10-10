using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftwareDefinedPlc.Core
{
    public interface IScanCycle
    {
        TimeSpan LastScanTime { get; }
        void RunOnce();
    }
}