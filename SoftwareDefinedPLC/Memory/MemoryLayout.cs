namespace SoftwareDefinedPlc.Memory;

/// <summary>MEM-9: a byte range inside M or D whose contents survive restarts.</summary>
public sealed record RetainRegion(MemoryArea Area, int ByteOffset, int ByteLength);

/// <summary>
/// MEM-1 / MEM-9. Area sizes (defaults: I 1024 B, Q 1024 B, M 65,536 B, D 262,144 B) and retain regions.
/// When <see cref="RetainRegions"/> is null the default is the last 8 KB of M, so low marker addresses
/// (%MD0 ...) stay ordinary scratch memory.
/// </summary>
public sealed record MemoryLayout
{
    public const int DefaultRetainBytes = 8 * 1024;

    public int InputBytes { get; init; } = 1024;
    public int OutputBytes { get; init; } = 1024;
    public int MarkerBytes { get; init; } = 65_536;
    public int DataBytes { get; init; } = 262_144;
    public IReadOnlyList<RetainRegion>? RetainRegions { get; init; }

    public int AreaBytes(MemoryArea area) => area switch
    {
        MemoryArea.I => InputBytes,
        MemoryArea.Q => OutputBytes,
        MemoryArea.M => MarkerBytes,
        _ => DataBytes,
    };

    public IReadOnlyList<RetainRegion> EffectiveRetainRegions
    {
        get
        {
            if (RetainRegions is not null) return RetainRegions;
            int len = Math.Min(DefaultRetainBytes, MarkerBytes);
            return len <= 0 ? Array.Empty<RetainRegion>() : new[] { new RetainRegion(MemoryArea.M, MarkerBytes - len, len) };
        }
    }

    public void Validate()
    {
        foreach (var a in Enum.GetValues<MemoryArea>())
            if (AreaBytes(a) <= 0) throw new ArgumentException($"Area {a} must have a positive size.");

        var regions = EffectiveRetainRegions;
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            if (r.Area is not (MemoryArea.M or MemoryArea.D))
                throw new ArgumentException($"Retain region {i}: only M and D can be retained, not {r.Area}.");
            if (r.ByteOffset < 0 || r.ByteLength <= 0 || (long)r.ByteOffset + r.ByteLength > AreaBytes(r.Area))
                throw new ArgumentException($"Retain region {i} ({r.Area}[{r.ByteOffset}..+{r.ByteLength}]) lies outside its area.");
            for (int j = 0; j < i; j++)
            {
                var o = regions[j];
                if (o.Area == r.Area && r.ByteOffset < o.ByteOffset + o.ByteLength && o.ByteOffset < r.ByteOffset + r.ByteLength)
                    throw new ArgumentException($"Retain regions {j} and {i} overlap.");
            }
        }
    }
}

/// <summary>MEM-3 data types.</summary>
public enum IecType
{
    Bool, SInt, USInt, Int, UInt, DInt, UDInt, LInt, ULInt,
    Byte, Word, DWord, LWord, Real, LReal, Time, String,
}

/// <summary>
/// Sizes and natural prefixes of the MEM-3 types. All multi-byte values are little-endian.
/// TIME is a signed 32-bit millisecond count (same as DINT). STRING is one length byte followed by
/// <c>maxLength</c> Latin-1 bytes (so it occupies maxLength + 1 bytes, maxLength 1..255, default 80).
/// </summary>
public static class IecTypes
{
    public const int DefaultStringLength = 80;

    public static int SizeOf(IecType type, int stringMaxLength = DefaultStringLength) => type switch
    {
        IecType.Bool or IecType.SInt or IecType.USInt or IecType.Byte => 1,
        IecType.Int or IecType.UInt or IecType.Word => 2,
        IecType.DInt or IecType.UDInt or IecType.DWord or IecType.Real or IecType.Time => 4,
        IecType.LInt or IecType.ULInt or IecType.LWord or IecType.LReal => 8,
        IecType.String => stringMaxLength + 1,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static SizePrefix PrefixOf(IecType type) => type switch
    {
        IecType.Bool => SizePrefix.X,
        IecType.SInt or IecType.USInt or IecType.Byte or IecType.String => SizePrefix.B,
        IecType.Int or IecType.UInt or IecType.Word => SizePrefix.W,
        IecType.DInt or IecType.UDInt or IecType.DWord or IecType.Real or IecType.Time => SizePrefix.D,
        _ => SizePrefix.L,
    };
}
