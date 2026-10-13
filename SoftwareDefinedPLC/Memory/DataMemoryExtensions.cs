namespace SoftwareDefinedPlc.Memory;

public static class DataMemoryExtensions
{
    /// <summary>Numeric view of an address by size prefix: X = 0/1, B = BYTE, W = INT, D = DINT, L = LINT.</summary>
    public static double ReadAsDouble(this IDataMemory m, Address a) => a.Size switch
    {
        SizePrefix.X => m.ReadBool(a) ? 1.0 : 0.0,
        SizePrefix.B => (double)m.ReadByte(a),
        SizePrefix.W => (double)m.ReadInt16(a),
        SizePrefix.D => (double)m.ReadInt32(a),
        _ => (double)m.ReadInt64(a),
    };

    /// <summary>Rounds and saturates to the width selected by the size prefix; NaN becomes 0.</summary>
    public static void WriteFromDouble(this IDataMemory m, Address a, double value)
    {
        if (double.IsNaN(value)) value = 0;
        double r = Math.Round(value);
        switch (a.Size)
        {
            case SizePrefix.X: m.WriteBool(a, value != 0); break;
            case SizePrefix.B: m.WriteByte(a, (byte)Math.Clamp(r, byte.MinValue, byte.MaxValue)); break;
            case SizePrefix.W: m.WriteInt16(a, (short)Math.Clamp(r, short.MinValue, short.MaxValue)); break;
            case SizePrefix.D: m.WriteInt32(a, (int)Math.Clamp(r, int.MinValue, int.MaxValue)); break;
            default:
                m.WriteInt64(a, r >= 9.2233720368547758E18 ? long.MaxValue : r <= -9.2233720368547758E18 ? long.MinValue : (long)r);
                break;
        }
    }

    /// <summary>TIME is a signed 32-bit millisecond count (%xD).</summary>
    public static TimeSpan ReadTime(this IDataMemory m, Address a) => TimeSpan.FromMilliseconds(m.ReadInt32(a));

    public static void WriteTime(this IDataMemory m, Address a, TimeSpan value) =>
        m.WriteInt32(a, (int)Math.Clamp(Math.Round(value.TotalMilliseconds), int.MinValue, int.MaxValue));

    public static object ReadValue(this IDataMemory m, Address a, IecType type, int stringMaxLength = IecTypes.DefaultStringLength) => type switch
    {
        IecType.Bool => m.ReadBool(a),
        IecType.SInt => m.ReadSByte(a),
        IecType.USInt or IecType.Byte => m.ReadByte(a),
        IecType.Int => m.ReadInt16(a),
        IecType.UInt or IecType.Word => m.ReadUInt16(a),
        IecType.DInt => m.ReadInt32(a),
        IecType.UDInt or IecType.DWord => m.ReadUInt32(a),
        IecType.LInt => m.ReadInt64(a),
        IecType.ULInt or IecType.LWord => m.ReadUInt64(a),
        IecType.Real => m.ReadSingle(a),
        IecType.LReal => m.ReadDouble(a),
        IecType.Time => m.ReadTime(a),
        IecType.String => m.ReadString(a, stringMaxLength),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>The value's CLR type must be the one <see cref="ReadValue"/> returns for the IEC type.</summary>
    public static void WriteValue(this IDataMemory m, Address a, IecType type, object value, int stringMaxLength = IecTypes.DefaultStringLength)
    {
        switch (type)
        {
            case IecType.Bool: m.WriteBool(a, (bool)value); break;
            case IecType.SInt: m.WriteSByte(a, (sbyte)value); break;
            case IecType.USInt: case IecType.Byte: m.WriteByte(a, (byte)value); break;
            case IecType.Int: m.WriteInt16(a, (short)value); break;
            case IecType.UInt: case IecType.Word: m.WriteUInt16(a, (ushort)value); break;
            case IecType.DInt: m.WriteInt32(a, (int)value); break;
            case IecType.UDInt: case IecType.DWord: m.WriteUInt32(a, (uint)value); break;
            case IecType.LInt: m.WriteInt64(a, (long)value); break;
            case IecType.ULInt: case IecType.LWord: m.WriteUInt64(a, (ulong)value); break;
            case IecType.Real: m.WriteSingle(a, (float)value); break;
            case IecType.LReal: m.WriteDouble(a, (double)value); break;
            case IecType.Time: m.WriteTime(a, (TimeSpan)value); break;
            case IecType.String: m.WriteString(a, (string)value, stringMaxLength); break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
}
