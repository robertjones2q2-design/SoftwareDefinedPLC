namespace SoftwareDefinedPlc.ProgrammingDevices
{
    public sealed class ProgrammingDevice : IProgrammingDevice
    {
        private IPlc? _plc;

        public void Connect(IPlc plc) => _plc = plc;     // no downcast: IPlc exposes Cpu
        public void Download(ILogicProgram program) => Require().Download(program);
        public void ConfigureTasks(IReadOnlyList<TaskConfig> tasks) => Require().ConfigureTasks(tasks);
        public void SetMode(CpuMode mode) => Require().SetMode(mode);
        public double MonitorWord(Address a) => Require().Cpu.InternalMemory.ReadWord(a);
        public bool MonitorBit(Address a) => Require().Cpu.InternalMemory.ReadBit(a);
        public void Force(Address a, bool value) => Require().Cpu.InternalMemory.WriteBit(a, value);

        private IPlc Require() => _plc ?? throw new InvalidOperationException("Not connected.");
    }
}
