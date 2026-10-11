namespace SoftwareDefinedPlc.Memory
{
    /// <summary>Holds named program instances that tasks bind to by name (TaskConfig.ProgramInstances).</summary>
    public interface IProgramMemory
    {
        int Count { get; }
        void Store(ILogicProgram program);          // replaces an instance with the same Name
        ILogicProgram? Find(string name);
        void Clear();
    }
}
