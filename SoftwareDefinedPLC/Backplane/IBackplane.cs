using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Bus
{
    public interface IBackplane
    {
        IReadOnlyList<IIoModule> Modules { get; }
        void Install(IIoModule module);
    }
}