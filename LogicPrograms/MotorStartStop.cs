using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.LogicPrograms
{
    // <summary>Start/stop motor seal-in: Q0 = (I0 OR Q0) AND NOT I1</summary>
    public sealed class MotorStartStop : ILogicProgram
    {
        public string Name => "MotorStartStop";
        public ProgramLanguage Language => ProgramLanguage.ST;
 
        public void Execute(IDataMemory i, IDataMemory q, IDataMemory m)
        {
            bool start = i.ReadBit(new Address(0));
            bool stop  = i.ReadBit(new Address(1));
            bool motor = q.ReadBit(new Address(0));
            q.WriteBit(new Address(0), (start || motor) && !stop);
        }
    }
}