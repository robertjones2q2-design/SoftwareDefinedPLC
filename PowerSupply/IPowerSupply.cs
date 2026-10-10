using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.PowerSupply
{
    public interface IPowerSupply
    {
        bool IsPowerGood { get; }
        double OutputVoltage { get; }     // e.g. 24 VDC
    }
}