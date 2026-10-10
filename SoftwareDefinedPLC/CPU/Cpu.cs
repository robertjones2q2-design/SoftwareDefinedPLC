using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftwareDefinedPlc.Core
{
    public sealed class Cpu
    {
        private readonly IPlc _plc;
        private readonly IPowerSupply _power;
        private readonly IReadOnlyList<ICommunicationInterface> _comms;
 
        public IDataMemory InputImage    { get; } = new DataMemory();
        public IDataMemory OutputImage   { get; } = new DataMemory();
        public IDataMemory InternalMemory { get; } = new DataMemory();
        public IScanCycle Scan { get; }
 
        public Cpu(IPlc plc, IPowerSupply power, IReadOnlyList<ICommunicationInterface> comms)
        {
            _plc = plc; _power = power; _comms = comms;
            Scan = new ScanCycle(this);
        }
 
        internal void ReadInputs()
        {
            foreach (var m in _plc.Backplane.Modules.OfType<IInputModule>())
                m.ReadInto(InputImage);
        }
 
        internal void ExecuteProgram() =>
            _plc.ProgramMemory.Current?.Execute(InputImage, OutputImage, InternalMemory);
 
        internal void WriteOutputs()
        {
            foreach (var m in _plc.Backplane.Modules.OfType<IOutputModule>())
                m.WriteFrom(OutputImage);
        }
 
        internal void Housekeeping()
        {
            foreach (var c in _comms) c.Service(InternalMemory);
        }
 
        public bool SelfCheck() =>
            _power.IsPowerGood &&
            _plc.Backplane.Modules.All(m => m.Status == ModuleStatus.Ok);
    }
}