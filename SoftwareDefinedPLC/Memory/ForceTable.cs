using System.Buffers.Binary;

namespace SoftwareDefinedPlc.Memory;

/// <summary>MEM-11. For a bit address Value is one byte (0 or 1); otherwise it has the width of the size prefix.</summary>
public sealed record ForceEntry(Address Address, byte[] Value, bool Enabled);

public interface IForceTable
{
    void Set(Address a, ReadOnlySpan<byte> value);
    void Clear(Address a);
    void ClearAll();
    IReadOnlyList<ForceEntry> Entries { get; }

    /// <summary>MEM-12: true while at least one enabled force exists.</summary>
    bool Active { get; }
    /// <summary>Raised with the new <see cref="Active"/> value whenever the enabled count goes from or to zero.</summary>
    event Action<bool> ActiveChanged;
}

/// <summary>
/// Only I and Q can be forced. Input forces are applied to the task's input image after latching (the program sees
/// them); output forces are applied only to the field-bound copy, so the program keeps seeing what it wrote.
/// Forces live here, not in memory, so they survive scans, Stop to Run and every restart type.
/// Mutation is copy-on-write: the scan path just reads an immutable array.
/// </summary>
public sealed class ForceTable : IForceTable
{
    private readonly object _gate = new();
    private readonly MemoryLayout _layout;
    private volatile ForceEntry[] _entries = Array.Empty<ForceEntry>();

    public ForceTable(MemoryLayout? layout = null) => _layout = layout ?? new MemoryLayout();

    public event Action<bool>? ActiveChanged;
    public IReadOnlyList<ForceEntry> Entries => _entries;
    public bool Active => ActiveCount > 0;
    public int ActiveCount => CountEnabled(_entries);

    public void Set(Address a, ReadOnlySpan<byte> value)
    {
        Validate(a);
        int expected = a.Size == SizePrefix.X ? 1 : a.ByteWidth;
        if (value.Length != expected)
            throw new ArgumentException($"Force value for {a} must be {expected} byte(s), got {value.Length}.", nameof(value));
        var entry = new ForceEntry(a, value.ToArray(), true);
        Mutate(cur =>
        {
            int i = Array.FindIndex(cur, e => e.Address == a);
            if (i < 0) return cur.Append(entry).ToArray();
            var copy = (ForceEntry[])cur.Clone();
            copy[i] = entry;
            return copy;
        });
    }

    /// <summary>Keeps the entry but stops applying it (or resumes it).</summary>
    public void SetEnabled(Address a, bool enabled) => Mutate(cur =>
        cur.Select(e => e.Address == a ? e with { Enabled = enabled } : e).ToArray());

    public void Clear(Address a) => Mutate(cur => cur.Where(e => e.Address != a).ToArray());
    public void ClearAll() => Mutate(_ => Array.Empty<ForceEntry>());

    /// <summary>Scan path: writes every enabled force of <paramref name="area"/> into <paramref name="image"/>. Allocation-free.</summary>
    public void Apply(MemoryArea area, IDataMemory image)
    {
        var entries = _entries;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.Enabled && e.Address.Area == area) image.WriteBytes(e.Address, e.Value);
        }
    }

    private void Validate(Address a)
    {
        if (a.Area is not (MemoryArea.I or MemoryArea.Q))
            throw new MemoryAccessException(a, "Only I and Q addresses can be forced");
        if (a.Size == SizePrefix.X ? (uint)a.BitOffset > 7 : a.BitOffset != 0)
            throw new MemoryAccessException(a, "Invalid bit offset");
        if (a.ByteOffset < 0 || (long)a.ByteOffset + a.ByteWidth > _layout.AreaBytes(a.Area))
            throw new MemoryAccessException(a, $"Outside area {a.Area}");
    }

    private void Mutate(Func<ForceEntry[], ForceEntry[]> change)
    {
        bool? raise = null;
        lock (_gate)
        {
            int before = CountEnabled(_entries);
            var next = change(_entries);
            _entries = next;
            int after = CountEnabled(next);
            if ((before == 0) != (after == 0)) raise = after > 0;
        }
        if (raise is bool active)
        {
            try { ActiveChanged?.Invoke(active); } catch { /* a diagnostics handler must not break forcing */ }
        }
    }

    private static int CountEnabled(ForceEntry[] entries)
    {
        int n = 0;
        for (int i = 0; i < entries.Length; i++) if (entries[i].Enabled) n++;
        return n;
    }
}

public static class ForceTableExtensions
{
    public static void SetBool(this IForceTable t, Address a, bool value)
    {
        Span<byte> b = stackalloc byte[1];
        b[0] = value ? (byte)1 : (byte)0;
        t.Set(a, b);
    }

    public static void SetInt16(this IForceTable t, Address a, short value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(b, value);
        t.Set(a, b);
    }

    public static void SetInt32(this IForceTable t, Address a, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        t.Set(a, b);
    }

    public static void SetSingle(this IForceTable t, Address a, float value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, value);
        t.Set(a, b);
    }
}
