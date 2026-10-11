namespace SoftwareDefinedPlc.Memory
{
    /// <summary>
    /// The input and output images a scan works against. Spec 2 extends this with
    /// LatchInputs/CommitOutputs (double buffering); Spec 1 only needs the two views.
    /// </summary>
    public interface IProcessImage
    {
        IDataMemory Inputs  { get; }
        IDataMemory Outputs { get; }
    }

    public sealed class ProcessImage : IProcessImage
    {
        public IDataMemory Inputs  { get; } = new DataMemory();
        public IDataMemory Outputs { get; } = new DataMemory();
    }
}
