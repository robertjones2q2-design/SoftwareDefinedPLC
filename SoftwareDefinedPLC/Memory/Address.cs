using System.Globalization;

namespace SoftwareDefinedPlc.Memory;

/// <summary>MEM-1: the four memory areas. Order matters (used as an array index).</summary>
public enum MemoryArea { I, Q, M, D }

/// <summary>MEM-2: IEC size prefixes. X = single bit, B = byte, W = 16 bit, D = 32 bit, L = 64 bit.</summary>
public enum SizePrefix { X, B, W, D, L }

/// <summary>MEM-10. Cold: everything zeroed. Warm: non-retain zeroed, retain kept. Hot: nothing changed.</summary>
public enum RestartType { Cold, Warm, Hot }

/// <summary>
/// MEM-2. Identifies area, size prefix, byte offset and (for X) the bit offset, in IEC notation
/// (<c>%IX0.3</c>, <c>%QB4</c>, <c>%MW10</c>, <c>%MD20</c>, <c>%ML32</c>).
/// </summary>
public readonly record struct Address(MemoryArea Area, SizePrefix Size, int ByteOffset, int BitOffset = 0)
{
    /// <summary>
    /// Spec 1 compatibility: a bit index. The area is not meaningful here; the legacy
    /// <c>ReadBit/WriteBit</c> shims on an I or Q image re-target it to that image's own area.
    /// </summary>
    [Obsolete("Spec 1 bit index. Use a typed address, e.g. Address.Parse(\"%IX0.1\").")]
    public Address(int index) : this(MemoryArea.M, SizePrefix.X, index >> 3, index & 7) { }

    /// <summary>Width in bytes of the addressed item (a bit address touches one byte).</summary>
    public int ByteWidth => Size switch
    {
        SizePrefix.X or SizePrefix.B => 1,
        SizePrefix.W => 2,
        SizePrefix.D => 4,
        _ => 8,
    };

    /// <summary>Absolute bit position inside the area (ByteOffset * 8 + BitOffset).</summary>
    public int BitIndex => ByteOffset * 8 + BitOffset;

    public static Address Parse(string iec) =>
        TryParse(iec, out var a) ? a : throw new FormatException($"'{iec}' is not a valid IEC address (expected e.g. %IX0.3, %QB4, %MW10).");

    /// <summary>Bit addresses (X) require a bit offset 0..7; every other size must not have one.</summary>
    public static bool TryParse(string? text, out Address address)
    {
        address = default;
        if (text is null) return false;
        var s = text.AsSpan().Trim();
        if (s.Length < 4 || s[0] != '%') return false;

        MemoryArea area;
        switch (char.ToUpperInvariant(s[1]))
        {
            case 'I': area = MemoryArea.I; break;
            case 'Q': area = MemoryArea.Q; break;
            case 'M': area = MemoryArea.M; break;
            case 'D': area = MemoryArea.D; break;
            default: return false;
        }

        SizePrefix size;
        switch (char.ToUpperInvariant(s[2]))
        {
            case 'X': size = SizePrefix.X; break;
            case 'B': size = SizePrefix.B; break;
            case 'W': size = SizePrefix.W; break;
            case 'D': size = SizePrefix.D; break;
            case 'L': size = SizePrefix.L; break;
            default: return false;
        }

        var rest = s.Slice(3);
        int dot = rest.IndexOf('.');
        var bytePart = dot < 0 ? rest : rest.Slice(0, dot);
        int bit = 0;
        if (size == SizePrefix.X)
        {
            if (dot < 0) return false;
            if (!int.TryParse(rest.Slice(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out bit) || bit > 7) return false;
        }
        else if (dot >= 0) return false;

        if (!int.TryParse(bytePart, NumberStyles.None, CultureInfo.InvariantCulture, out int offset)) return false;
        address = new Address(area, size, offset, bit);
        return true;
    }

    public override string ToString() =>
        Size == SizePrefix.X ? $"%{Area}{Size}{ByteOffset}.{BitOffset}" : $"%{Area}{Size}{ByteOffset}";
}

/// <summary>MEM-5: out-of-range or malformed access. Carries the offending address.</summary>
public sealed class MemoryAccessException : Exception
{
    public Address Address { get; }
    public MemoryAccessException(Address address, string message) : base($"{message} ({address})") => Address = address;
}

/// <summary>
/// The bits a module occupies in the I or Q image. Bit-granular so two digital modules may share a byte
/// (%IX0.0..0.3 and %IX0.4..0.7); analog modules simply occupy whole bytes. Used for MEM-13 overlap checks.
/// </summary>
public readonly record struct ImageRange(MemoryArea Area, int StartBit, int BitLength)
{
    /// <summary>Exclusive end bit.</summary>
    public int EndBit => StartBit + BitLength;

    public bool Overlaps(ImageRange other) =>
        Area == other.Area && BitLength > 0 && other.BitLength > 0 &&
        StartBit < other.EndBit && other.StartBit < EndBit;

    public override string ToString()
    {
        if (BitLength <= 0) return $"%{Area}[empty]";
        int last = EndBit - 1;
        return $"%{Area}X{StartBit >> 3}.{StartBit & 7}..{last >> 3}.{last & 7}";
    }
}
