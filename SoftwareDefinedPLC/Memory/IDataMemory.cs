namespace SoftwareDefinedPlc.Memory;

/// <summary>
/// MEM-5. Typed, bit-exact, little-endian access. The size prefix of the address must match the width
/// of the type (ReadInt16 needs %xW, ReadInt32/ReadSingle need %xD, ...), bools need %xX.y.
/// Anything out of range throws <see cref="MemoryAccessException"/>. No arithmetic happens here (MEM-4).
/// </summary>
public interface IDataMemory
{
    bool ReadBool(Address a);       void WriteBool(Address a, bool value);
    byte ReadByte(Address a);       void WriteByte(Address a, byte value);        // BYTE, USINT
    sbyte ReadSByte(Address a);     void WriteSByte(Address a, sbyte value);      // SINT
    short ReadInt16(Address a);     void WriteInt16(Address a, short value);      // INT
    ushort ReadUInt16(Address a);   void WriteUInt16(Address a, ushort value);    // UINT, WORD
    int ReadInt32(Address a);       void WriteInt32(Address a, int value);        // DINT
    uint ReadUInt32(Address a);     void WriteUInt32(Address a, uint value);      // UDINT, DWORD
    long ReadInt64(Address a);      void WriteInt64(Address a, long value);       // LINT
    ulong ReadUInt64(Address a);    void WriteUInt64(Address a, ulong value);     // ULINT, LWORD
    float ReadSingle(Address a);    void WriteSingle(Address a, float value);     // REAL
    double ReadDouble(Address a);   void WriteDouble(Address a, double value);    // LREAL

    string ReadString(Address a, int maxLength = 80);
    void WriteString(Address a, string value, int maxLength = 80);

    /// <summary>Block copy starting at the address. For a bit address exactly one byte (0 or 1) is transferred.</summary>
    void ReadBytes(Address a, Span<byte> dest);
    void WriteBytes(Address a, ReadOnlySpan<byte> src);

    // ---- MEM-6: Spec 1 shims, kept for one release ------------------------------------------------

    [Obsolete("Spec 1 shim; use ReadBool.")]
    bool ReadBit(Address a);
    [Obsolete("Spec 1 shim; use WriteBool.")]
    void WriteBit(Address a, bool value);
    /// <summary>Returns the value as a double, converted according to the size prefix (W = INT, D = DINT, ...).</summary>
    [Obsolete("Spec 1 shim; use the typed accessors.")]
    double ReadWord(Address a);
    [Obsolete("Spec 1 shim; use the typed accessors.")]
    void WriteWord(Address a, double value);
}

/// <summary>MEM-9: capture and restore the retained bytes (consumed by Spec 7 for persistence).</summary>
public interface IRetainStore
{
    /// <summary>All retain regions, concatenated in declaration order, as one consistent snapshot.</summary>
    byte[] Capture();

    /// <summary>The data must have exactly the length produced by <see cref="Capture"/> for the current layout.</summary>
    void Restore(ReadOnlySpan<byte> data);
}
