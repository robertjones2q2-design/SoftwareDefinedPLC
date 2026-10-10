using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc
{
    // ------------------------------------------------------------------
    // Enums / value types
    // ------------------------------------------------------------------
    public enum CpuMode { Stop, Run, Fault }
    public enum ModuleStatus { Ok, Fault, NotPresent }
    public enum OutputType { Relay, Transistor, Triac }
    public enum ProgramLanguage { LD, FBD, ST, IL, SFC }   // IEC 61131-3
 
    public readonly record struct Address(int Index);       // simple bit/word address
 
    // ------------------------------------------------------------------
    // Field-side abstractions (sensors and actuators)
    // ------------------------------------------------------------------
    public interface IDigitalSource { bool Read(); }                // pushbutton, prox switch...
    public interface IAnalogSource  { double Read(); }              // 4-20 mA, 0-10 V transmitter
    public interface IDigitalSink   { void Write(bool value); }     // contactor, solenoid, lamp
    public interface IAnalogSink    { void Write(double value); }   // VFD reference, proportional valve
 
    // ------------------------------------------------------------------
    // PLC aggregate
    // ------------------------------------------------------------------
    public interface IPlc
    {
        CpuMode Mode { get; }
        IProgramMemory ProgramMemory { get; }
        IDataMemory DataMemory { get; }
        IBackplane Backplane { get; }
        void SetMode(CpuMode mode);
        void Download(ILogicProgram program);
    }
 
    // ---- PLC aggregate -----------------------------------------------
 
    public sealed class Plc : IPlc
    {
        public CpuMode Mode { get; private set; } = CpuMode.Stop;
        public IProgramMemory ProgramMemory { get; } = new ProgramMemory();
        public IDataMemory DataMemory => Cpu.InternalMemory;
        public IBackplane Backplane { get; } = new Backplane();
        public IPowerSupply Power { get; }
        public IList<ICommunicationInterface> Communications { get; } = new List<ICommunicationInterface>();
        public Cpu Cpu { get; }
 
        public Plc(IPowerSupply power)
        {
            Power = power;
            Cpu = new Cpu(this, power, (IReadOnlyList<ICommunicationInterface>)Communications);
        }
 
        public void SetMode(CpuMode mode)
        {
            if (mode == CpuMode.Run && !Cpu.SelfCheck()) { Mode = CpuMode.Fault; return; }
            Mode = mode;
        }
 
        public void Download(ILogicProgram program)
        {
            if (Mode == CpuMode.Run) throw new InvalidOperationException("Stop the CPU before download.");
            ProgramMemory.Store(program);
        }
 
        /// <summary>Call from a host loop / timer while in Run mode.</summary>
        public void Tick()
        {
            if (Mode == CpuMode.Run) Cpu.Scan.RunOnce();
        }
    }
}