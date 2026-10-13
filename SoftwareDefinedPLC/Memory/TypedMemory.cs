using System.Buffers.Binary;
using System.Text;

namespace SoftwareDefinedPlc.Memory;

/// <summary>
/// Implements the whole <see cref="IDataMemory"/> surface (validation, little-endian encoding, bit access)
/// on top of two primitives. Subclasses decide where the bytes live and how they are locked.
/// Nothing here allocates except ReadString/WriteString.
/// </summary>
public abstract class TypedMemory : IDataMemory
{
    /// <summary>Size in bytes of the area, or -1 if this memory does not serve it.</summary>
    protected abstract int AreaSize(MemoryArea area);

    /// <summary>Address and length are already validated. For a bit address dest.Length == 1 and receives 0 or 1.</summary>
    protected abstract void ReadCore(Address a, Span<byte> dest);

    /// <summary>Address and length are already validated. For a bit address src[0] != 0 sets the bit.</summary>
    protected abstract void WriteCore(Address a, ReadOnlySpan<byte> src);

    /// <summary>Spec 1 shims only: lets an area-bound image re-target a legacy bit index to its own area.</summary>
    protected virtual Address ResolveLegacy(Address a) => a;

    protected static void CopyOut(byte[] data, Address a, Span<byte> dest)
    {
        if (a.Size == SizePrefix.X) dest[0] = (byte)((data[a.ByteOffset] >> a.BitOffset) & 1);
        else data.AsSpan(a.ByteOffset, dest.Length).CopyTo(dest);
    }

    protected static void CopyIn(byte[] data, Address a, ReadOnlySpan<byte> src)
    {
        if (a.Size == SizePrefix.X)
        {
            int mask = 1 << a.BitOffset;
            data[a.ByteOffset] = src[0] != 0 ? (byte)(data[a.ByteOffset] | mask) : (byte)(data[a.ByteOffset] & ~mask);
        }
        else src.CopyTo(data.AsSpan(a.ByteOffset, src.Length));
    }

    // ------------------------------------------------------------ validation

    private void Validate(Address a, int length)
    {
        int size = AreaSize(a.Area);
        if (size < 0) throw new MemoryAccessException(a, $"Area {a.Area} is not available in this memory");
        if (a.Size == SizePrefix.X)
        {
            if ((uint)a.BitOffset > 7) throw new MemoryAccessException(a, "Bit offset must be 0..7");
        }
        else if (a.BitOffset != 0) throw new MemoryAccessException(a, "Only bit addresses (X) may have a bit offset");
        if (a.ByteOffset < 0 || (long)a.ByteOffset + length > size)
            throw new MemoryAccessException(a, $"Access of {length} byte(s) is outside area {a.Area} (size {size})");
    }

    private void Typed(Address a, int width)
    {
        if (a.Size == SizePrefix.X || a.ByteWidth != width)
            throw new MemoryAccessException(a, $"Size prefix {a.Size} does not match a {width * 8}-bit value");
        Validate(a, width);
    }

    // ------------------------------------------------------------ BOOL / 8-bit

    public bool ReadBool(Address a)
    {
        if (a.Size != SizePrefix.X) throw new MemoryAccessException(a, "BOOL needs a bit address (X)");
        Validate(a, 1);
        Span<byte> b = stackalloc byte[1];
        ReadCore(a, b);
        return b[0] != 0;
    }

    public void WriteBool(Address a, bool value)
    {
        if (a.Size != SizePrefix.X) throw new MemoryAccessException(a, "BOOL needs a bit address (X)");
        Validate(a, 1);
        Span<byte> b = stackalloc byte[1];
        b[0] = value ? (byte)1 : (byte)0;
        WriteCore(a, b);
    }

    public byte ReadByte(Address a)
    {
        Typed(a, 1);
        Span<byte> b = stackalloc byte[1];
        ReadCore(a, b);
        return b[0];
    }

    public void WriteByte(Address a, byte value)
    {
        Typed(a, 1);
        Span<byte> b = stackalloc byte[1];
        b[0] = value;
        WriteCore(a, b);
    }

    public sbyte ReadSByte(Address a) => unchecked((sbyte)ReadByte(a));
    public void WriteSByte(Address a, sbyte value) => WriteByte(a, unchecked((byte)value));

    // ------------------------------------------------------------ 16 bit

    public ushort ReadUInt16(Address a)
    {
        Typed(a, 2);
        Span<byte> b = stackalloc byte[2];
        ReadCore(a, b);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    public void WriteUInt16(Address a, ushort value)
    {
        Typed(a, 2);
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        WriteCore(a, b);
    }

    public short ReadInt16(Address a) => unchecked((short)ReadUInt16(a));
    public void WriteInt16(Address a, short value) => WriteUInt16(a, unchecked((ushort)value));

    // ------------------------------------------------------------ 32 bit

    public uint ReadUInt32(Address a)
    {
        Typed(a, 4);
        Span<byte> b = stackalloc byte[4];
        ReadCore(a, b);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    public void WriteUInt32(Address a, uint value)
    {
        Typed(a, 4);
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        WriteCore(a, b);
    }

    public int ReadInt32(Address a) => unchecked((int)ReadUInt32(a));
    public void WriteInt32(Address a, int value) => WriteUInt32(a, unchecked((uint)value));
    public float ReadSingle(Address a) => BitConverter.UInt32BitsToSingle(ReadUInt32(a));
    public void WriteSingle(Address a, float value) => WriteUInt32(a, BitConverter.SingleToUInt32Bits(value));

    // ------------------------------------------------------------ 64 bit

    public ulong ReadUInt64(Address a)
    {
        Typed(a, 8);
        Span<byte> b = stackalloc byte[8];
        ReadCore(a, b);
        return BinaryPrimitives.ReadUInt64LittleEndian(b);
    }

    public void WriteUInt64(Address a, ulong value)
    {
        Typed(a, 8);
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        WriteCore(a, b);
    }

    public long ReadInt64(Address a) => unchecked((long)ReadUInt64(a));
    public void WriteInt64(Address a, long value) => WriteUInt64(a, unchecked((ulong)value));
    public double ReadDouble(Address a) => BitConverter.UInt64BitsToDouble(ReadUInt64(a));
    public void WriteDouble(Address a, double value) => WriteUInt64(a, BitConverter.DoubleToUInt64Bits(value));

    // ------------------------------------------------------------ STRING

    public string ReadString(Address a, int maxLength = IecTypes.DefaultStringLength)
    {
        CheckString(a, maxLength);
        var buf = new byte[maxLength + 1];
        ReadCore(a, buf);
        int len = Math.Min((int)buf[0], maxLength);
        return Encoding.Latin1.GetString(buf, 1, len);
    }

    public void WriteString(Address a, string value, int maxLength = IecTypes.DefaultStringLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckString(a, maxLength);
        var buf = new byte[maxLength + 1];                 // zero padded, so stale characters never survive
        int n = Encoding.Latin1.GetBytes(value.AsSpan(0, Math.Min(value.Length, maxLength)), buf.AsSpan(1));
        buf[0] = (byte)n;
        WriteCore(a, buf);
    }

    private void CheckString(Address a, int maxLength)
    {
        if (maxLength < 1 || maxLength > 255) throw new ArgumentOutOfRangeException(nameof(maxLength), "STRING length must be 1..255.");
        if (a.Size != SizePrefix.B) throw new MemoryAccessException(a, "STRING needs a byte address (B)");
        Validate(a, maxLength + 1);
    }

    // ------------------------------------------------------------ blocks

    public void ReadBytes(Address a, Span<byte> dest)
    {
        BlockCheck(a, dest.Length);
        ReadCore(a, dest);
    }

    public void WriteBytes(Address a, ReadOnlySpan<byte> src)
    {
        BlockCheck(a, src.Length);
        WriteCore(a, src);
    }

    private void BlockCheck(Address a, int length)
    {
        if (a.Size == SizePrefix.X)
        {
            if (length != 1) throw new MemoryAccessException(a, "A bit address transfers exactly one byte (0 or 1)");
            Validate(a, 1);
        }
        else Validate(a, length);
    }

    // ------------------------------------------------------------ MEM-6 shims

    [Obsolete("Spec 1 shim; use ReadBool.")]
    public bool ReadBit(Address a) => ReadBool(ResolveLegacy(a));

    [Obsolete("Spec 1 shim; use WriteBool.")]
    public void WriteBit(Address a, bool value) => WriteBool(ResolveLegacy(a), value);

    [Obsolete("Spec 1 shim; use the typed accessors.")]
    public double ReadWord(Address a) => this.ReadAsDouble(ResolveLegacy(a));

    [Obsolete("Spec 1 shim; use the typed accessors.")]
    public void WriteWord(Address a, double value) => this.WriteFromDouble(ResolveLegacy(a), value);
}
