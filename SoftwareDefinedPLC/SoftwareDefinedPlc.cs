namespace SoftwareDefinedPlc
{
    // ------------------------------------------------------------------
    // Enums / value types
    // ------------------------------------------------------------------
    /// <summary>Startup is last so default(CpuMode) stays Stop.</summary>
    public enum CpuMode { Stop, Run, Fault, Startup }
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
        Cpu Cpu { get; }
        IProgramMemory ProgramMemory { get; }
        IDataMemory DataMemory { get; }
        IBackplane Backplane { get; }

        /// <summary>Why the last mode request was refused (null if it was accepted).</summary>
        string? LastModeError { get; }

        void SetMode(CpuMode mode);
        void Download(ILogicProgram program);

        /// <summary>Temporary until Spec 3 delivers tasks inside the project package (tasks.json).</summary>
        void ConfigureTasks(IReadOnlyList<TaskConfig> tasks);
    }

    // ---- PLC aggregate -----------------------------------------------

    public sealed class Plc : IPlc
    {
        private readonly List<ICommunicationInterface> _comms = new();

        public IProgramMemory ProgramMemory { get; } = new ProgramMemory();
        public IDataMemory DataMemory => Cpu.InternalMemory;
        public IBackplane Backplane { get; } = new Backplane();
        public IPowerSupply Power { get; }
        public IList<ICommunicationInterface> Communications => _comms;
        public Cpu Cpu { get; }
        public CpuMode Mode => Cpu.Mode;
        public string? LastModeError { get; private set; }

        public Plc(IPowerSupply power, SchedulerOptions? options = null)
        {
            Power = power;
            Cpu = new Cpu(power, Backplane, ProgramMemory, _comms, options);
            Cpu.CompleteStartup();      // no hardware configuration to load yet (Spec 7)
        }

        public void SetMode(CpuMode mode)
        {
            LastModeError = null;
            switch (mode)
            {
                case CpuMode.Run:
                    if (!Cpu.TryEnterRun(out var runError)) LastModeError = runError;
                    break;
                case CpuMode.Stop:
                    if (!Cpu.RequestStop("Operator command", out var stopError)) LastModeError = stopError;
                    break;
                default:
                    throw new ArgumentException($"{mode} cannot be requested; the runtime enters it itself.", nameof(mode));
            }
        }

        public void Download(ILogicProgram program)
        {
            if (Mode == CpuMode.Run) throw new InvalidOperationException("Stop the CPU before download.");
            ProgramMemory.Store(program);
        }

        public void ConfigureTasks(IReadOnlyList<TaskConfig> tasks)
        {
            if (Mode == CpuMode.Run) throw new InvalidOperationException("Stop the CPU before changing tasks.");
            Cpu.Scheduler.Configure(tasks);
        }

        /// <summary>
        /// Deterministic unit tests only (SCAN-1): runs one scan of every task. Throws unless the Plc was
        /// built with <see cref="SchedulerOptions.Deterministic"/>. Production hosts start the runtime and wait.
        /// </summary>
        public void Tick() => Cpu.Tick();
    }
}
