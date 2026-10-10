using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.ProgrammingDevices
{
    public interface IProgrammingDevice
    {
        void Connect(IPlc plc);
        void Download(ILogicProgram program);
        void SetMode(CpuMode mode);
        double MonitorWord(Address a);
        bool MonitorBit(Address a);
        void Force(Address a, bool value);
    }
}