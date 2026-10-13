namespace SoftwareDefinedPlc.ProgrammingDevices
{
    public interface IProgrammingDevice
    {
        void Connect(IPlc plc);
        void Download(ILogicProgram program);
        void ConfigureTasks(IReadOnlyList<TaskConfig> tasks);   // temporary, see IPlc
        void SetMode(CpuMode mode);
        double MonitorWord(Address a);
        bool MonitorBit(Address a);
        /// <summary>Forces an I or Q bit through the force table (MEM-11); it persists until cleared.</summary>
        void Force(Address a, bool value);
        void ClearForce(Address a);
    }
}
