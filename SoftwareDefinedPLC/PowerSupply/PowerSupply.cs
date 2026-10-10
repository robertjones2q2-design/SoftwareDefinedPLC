using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Power
{
    public sealed class PowerSupply : IPowerSupply
    {
        public bool IsPowerGood { get; set; } = true;
        public double OutputVoltage { get; init; } = 24.0;
    }
}