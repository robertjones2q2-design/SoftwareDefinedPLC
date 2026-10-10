using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.ProgrammingDevices
{
    public sealed class ProgrammingDevice : IProgrammingDevice
    {
        private Plc? _plc;
 
        public void Connect(IPlc plc) => _plc = (Plc)plc;
        public void Download(ILogicProgram program) => Require().Download(program);
        public void SetMode(CpuMode mode) => Require().SetMode(mode);
        public double MonitorWord(Address a) => Require().Cpu.InternalMemory.ReadWord(a);
        public bool MonitorBit(Address a) => Require().Cpu.InternalMemory.ReadBit(a);
        public void Force(Address a, bool value) => Require().Cpu.InternalMemory.WriteBit(a, value);
 
        private Plc Require() => _plc ?? throw new InvalidOperationException("Not connected.");
    }
}