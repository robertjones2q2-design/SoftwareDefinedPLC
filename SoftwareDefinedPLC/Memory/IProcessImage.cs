namespace SoftwareDefinedPlc.Memory;

/// <summary>
/// MEM-7. One task's private view of the I/O images. A scan works only against these copies:
/// <see cref="LatchInputs"/> at scan start, <see cref="CommitOutputs"/> at scan end.
/// </summary>
public interface IProcessImage
{
    /// <summary>Input image as the program sees it (physical values with input forces applied). Stable for the whole scan.</summary>
    IDataMemory Inputs { get; }

    /// <summary>Output image as the program sees it (what it wrote; never altered by forces).</summary>
    IDataMemory Outputs { get; }

    /// <summary>What modules write to the field: <see cref="Outputs"/> with output forces applied (MEM-11). Valid after <see cref="CommitOutputs"/>.</summary>
    IDataMemory FieldOutputs { get; }

    /// <summary>
    /// Scan start: refreshes the private output copy from the shared Q image, rebuilds the private input copy from the
    /// modules, applies input forces, then publishes the result to the shared I image.
    /// </summary>
    void LatchInputs(IReadOnlyList<IInputModule> modules);

    /// <summary>Scan end: commits the program's outputs to the shared Q image in one atomic block write, then builds the field copy.</summary>
    void CommitOutputs();
}

public sealed class ProcessImage : IProcessImage
{
    private readonly DataMemory _shared;
    private readonly ForceTable? _forces;
    private readonly ImageBuffer _in, _out, _field;
    private readonly Address _iStart = new(MemoryArea.I, SizePrefix.B, 0);
    private readonly Address _qStart = new(MemoryArea.Q, SizePrefix.B, 0);

    public ProcessImage(DataMemory shared, ForceTable? forces = null)
    {
        _shared = shared;
        _forces = forces;
        _in = new ImageBuffer(MemoryArea.I, shared.Layout.InputBytes);
        _out = new ImageBuffer(MemoryArea.Q, shared.Layout.OutputBytes);
        _field = new ImageBuffer(MemoryArea.Q, shared.Layout.OutputBytes);
    }

    public IDataMemory Inputs => _in;
    public IDataMemory Outputs => _out;
    public IDataMemory FieldOutputs => _field;

    public void LatchInputs(IReadOnlyList<IInputModule> modules)
    {
        // Scans are serialized by the scan gate, so the shared Q image cannot change between this refresh and the commit.
        _shared.ReadBytes(_qStart, _out.Bytes);

        _in.Clear();                                   // rebuilt from the modules every scan, so a cleared force never lingers
        for (int i = 0; i < modules.Count; i++) modules[i].ReadInto(_in);
        _forces?.Apply(MemoryArea.I, _in);
        _shared.WriteBytes(_iStart, _in.Bytes);
    }

    public void CommitOutputs()
    {
        _shared.WriteBytes(_qStart, _out.Bytes);       // the program's view, unforced
        _field.CopyFrom(_out);
        _forces?.Apply(MemoryArea.Q, _field);          // after execute, before module write
    }
}