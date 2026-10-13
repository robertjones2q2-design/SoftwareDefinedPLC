namespace SoftwareDefinedPlc.Tests;

static class Addr
{
    public static readonly Address I00 = Address.Parse("%IX0.0");
    public static readonly Address Q00 = Address.Parse("%QX0.0");
    public static readonly Address NonRetainW = Address.Parse("%MW0");
    public static readonly Address RetainW = Address.Parse("%MW57344");   // first byte of the default retain region
    public static readonly Address DataW = Address.Parse("%DW0");
}

sealed class LambdaProgram(string name, Action<ScanContext> body) : ILogicProgram
{
    public string Name => name;
    public ProgramLanguage Language => ProgramLanguage.ST;
    public void Execute(in ScanContext ctx) => body(ctx);
}

// ---- MEM-2: addressing --------------------------------------------------------------------

public class AddressTests
{
    [Theory]
    [InlineData("%IX0.3", MemoryArea.I, SizePrefix.X, 0, 3)]
    [InlineData("%QB4", MemoryArea.Q, SizePrefix.B, 4, 0)]
    [InlineData("%MW10", MemoryArea.M, SizePrefix.W, 10, 0)]
    [InlineData("%MD20", MemoryArea.M, SizePrefix.D, 20, 0)]
    [InlineData("%ML32", MemoryArea.M, SizePrefix.L, 32, 0)]
    [InlineData("%DD4", MemoryArea.D, SizePrefix.D, 4, 0)]
    [InlineData("%qx7.7", MemoryArea.Q, SizePrefix.X, 7, 7)]
    public void Parse_and_ToString_round_trip(string text, MemoryArea area, SizePrefix size, int byteOffset, int bit)
    {
        var a = Address.Parse(text);
        Assert.Equal(new Address(area, size, byteOffset, bit), a);
        Assert.Equal(a, Address.Parse(a.ToString()));
    }

    [Theory]
    [InlineData("")] [InlineData("MW10")] [InlineData("%ZW1")] [InlineData("%MZ1")] [InlineData("%IX0")]
    [InlineData("%IX0.8")] [InlineData("%MW10.1")] [InlineData("%MW")] [InlineData("%MW-1")]
    public void Invalid_addresses_are_rejected(string text)
    {
        Assert.False(Address.TryParse(text, out _));
        Assert.Throws<FormatException>(() => Address.Parse(text));
    }

    [Fact]
    public void ToString_uses_iec_notation()
    {
        Assert.Equal("%IX0.3", new Address(MemoryArea.I, SizePrefix.X, 0, 3).ToString());
        Assert.Equal("%MW10", new Address(MemoryArea.M, SizePrefix.W, 10).ToString());
    }
}

// ---- MEM-3/4/5: types, bit-exactness, bounds -----------------------------------------------

public class TypedMemoryTests
{
    static object RandomValue(IecType t, Random r) => t switch
    {
        IecType.Bool => r.Next(2) == 1,
        IecType.SInt => (sbyte)r.Next(-128, 128),
        IecType.USInt or IecType.Byte => (byte)r.Next(256),
        IecType.Int => (short)r.Next(short.MinValue, short.MaxValue + 1),
        IecType.UInt or IecType.Word => (ushort)r.Next(65536),
        IecType.DInt => r.Next(int.MinValue, int.MaxValue),
        IecType.UDInt or IecType.DWord => (uint)r.NextInt64(0, uint.MaxValue + 1L),
        IecType.LInt => r.NextInt64(long.MinValue, long.MaxValue),
        IecType.ULInt or IecType.LWord => (ulong)r.NextInt64(long.MinValue, long.MaxValue) ^ (ulong)r.Next(),
        IecType.Real => (float)(r.NextDouble() * 2e6 - 1e6),
        IecType.LReal => r.NextDouble() * 2e12 - 1e12,
        IecType.Time => TimeSpan.FromMilliseconds(r.Next(-1_000_000, 1_000_000)),
        _ => new string(Enumerable.Range(0, r.Next(0, 81)).Select(_ => (char)r.Next(32, 127)).ToArray()),
    };

    [Fact]
    public void Every_type_round_trips_at_random_addresses_including_boundaries()
    {
        var mem = new DataMemory();
        var rnd = new Random(12345);
        foreach (var area in new[] { MemoryArea.M, MemoryArea.D })
            foreach (var type in Enum.GetValues<IecType>())
                for (int i = 0; i < 300; i++)
                {
                    int size = IecTypes.SizeOf(type);
                    int areaBytes = mem.Layout.AreaBytes(area);
                    int bit = type == IecType.Bool ? rnd.Next(8) : 0;
                    int offset = i switch { 0 => 0, 1 => areaBytes - size, _ => rnd.Next(0, areaBytes - size + 1) };
                    if (type == IecType.Bool) offset = Math.Min(offset, areaBytes - 1);
                    var a = new Address(area, IecTypes.PrefixOf(type), offset, bit);
                    var v = RandomValue(type, rnd);

                    mem.WriteValue(a, type, v);
                    Assert.Equal(v, mem.ReadValue(a, type));
                }
    }

    [Fact]
    public void Writing_a_value_leaves_neighbouring_bytes_alone()
    {
        var mem = new DataMemory();
        for (int i = 0; i < 16; i++) mem.WriteByte(Address.Parse($"%MB{i}"), 0xAA);
        mem.WriteInt32(Address.Parse("%MD4"), -2);
        Assert.Equal(0xAA, mem.ReadByte(Address.Parse("%MB3")));
        Assert.Equal(0xAA, mem.ReadByte(Address.Parse("%MB8")));
        Assert.Equal(0xFE, mem.ReadByte(Address.Parse("%MB4")));
    }

    [Fact]
    public void Writing_INT_minus_one_reads_back_as_0xFFFF_and_255_255()
    {
        var mem = new DataMemory();
        mem.WriteInt16(Address.Parse("%MW0"), -1);
        Assert.Equal((ushort)0xFFFF, mem.ReadUInt16(Address.Parse("%MW0")));
        Assert.Equal(255, mem.ReadByte(Address.Parse("%MB0")));
        Assert.Equal(255, mem.ReadByte(Address.Parse("%MB1")));
    }

    [Fact]
    public void Layout_is_little_endian()
    {
        var mem = new DataMemory();
        mem.WriteUInt32(Address.Parse("%MD0"), 0x01020304);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, Enumerable.Range(0, 4).Select(i => mem.ReadByte(Address.Parse($"%MB{i}"))).ToArray());
    }

    [Fact]
    public void Bit_writes_do_not_disturb_other_bits()
    {
        var mem = new DataMemory();
        mem.WriteByte(Address.Parse("%MB0"), 0b1010_1010);
        mem.WriteBool(Address.Parse("%MX0.0"), true);
        mem.WriteBool(Address.Parse("%MX0.7"), false);
        Assert.Equal((byte)0b0010_1011, mem.ReadByte(Address.Parse("%MB0")));
        Assert.True(mem.ReadBool(Address.Parse("%MX0.1")));
    }

    [Fact]
    public void Out_of_range_access_throws_with_the_address()
    {
        var mem = new DataMemory();
        var last = new Address(MemoryArea.M, SizePrefix.D, mem.Layout.MarkerBytes - 4);
        mem.WriteInt32(last, 7);
        var past = new Address(MemoryArea.M, SizePrefix.D, mem.Layout.MarkerBytes - 3);
        var ex = Assert.Throws<MemoryAccessException>(() => mem.ReadInt32(past));
        Assert.Equal(past, ex.Address);
        Assert.Throws<MemoryAccessException>(() => mem.ReadInt32(new Address(MemoryArea.M, SizePrefix.D, -1)));
        Assert.Throws<MemoryAccessException>(() => mem.WriteBool(new Address(MemoryArea.I, SizePrefix.X, 1024, 0), true));
    }

    [Fact]
    public void Size_prefix_must_match_the_type()
    {
        var mem = new DataMemory();
        Assert.Throws<MemoryAccessException>(() => mem.ReadInt32(Address.Parse("%MW0")));
        Assert.Throws<MemoryAccessException>(() => mem.ReadBool(Address.Parse("%MB0")));
        Assert.Throws<MemoryAccessException>(() => mem.ReadByte(Address.Parse("%MX0.0")));
    }

    [Fact]
    public void Image_buffer_serves_only_its_own_area()
    {
        var img = new ImageBuffer(MemoryArea.I, 16);
        img.WriteBool(Address.Parse("%IX0.1"), true);
        Assert.Throws<MemoryAccessException>(() => img.ReadBool(Address.Parse("%MX0.1")));
    }

    [Fact]
    public void Block_copy_and_strings_work()
    {
        var mem = new DataMemory();
        mem.WriteBytes(Address.Parse("%DB10"), new byte[] { 1, 2, 3 });
        var dest = new byte[3];
        mem.ReadBytes(Address.Parse("%DB10"), dest);
        Assert.Equal(new byte[] { 1, 2, 3 }, dest);

        var s = Address.Parse("%MB100");
        mem.WriteString(s, "hello world", 5);
        Assert.Equal("hello", mem.ReadString(s, 5));
        mem.WriteString(s, "hi", 5);
        Assert.Equal("hi", mem.ReadString(s, 5));
    }

    [Fact]
    public void Legacy_shims_still_work_and_honour_size_prefix()
    {
#pragma warning disable CS0618
        var mem = new DataMemory();
        mem.WriteInt32(Address.Parse("%MD0"), 76);
        Assert.Equal(76d, mem.ReadWord(Address.Parse("%MD0")));
        mem.WriteWord(Address.Parse("%MW4"), 40000.0);                 // saturates to INT
        Assert.Equal((double)short.MaxValue, mem.ReadWord(Address.Parse("%MW4")));
        mem.WriteBit(new Address(9), true);                              // legacy bit index 9 = M byte 1 bit 1
        Assert.True(mem.ReadBool(Address.Parse("%MX1.1")));
#pragma warning restore CS0618
    }
}

// ---- MEM-8: concurrency -------------------------------------------------------------------

public class ConcurrencyTests
{
    [Fact]
    public void Readers_never_observe_a_half_written_value()
    {
        const int A = 0x11111111, B = 0x22222222;
        const long LA = 0x1111111111111111L, LB = 0x2222222222222222L;
        var mem = new DataMemory();
        var d32 = Address.Parse("%MD100");
        var d64 = Address.Parse("%ML200");
        int stop = 0, torn = 0;

        var readers = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                int v = mem.ReadInt32(d32);
                long l = mem.ReadInt64(d64);
                if (v != 0 && v != A && v != B) Interlocked.Increment(ref torn);
                if (l != 0 && l != LA && l != LB) Interlocked.Increment(ref torn);
            }
        })).ToArray();
        foreach (var t in readers) t.Start();

        for (int outer = 0; outer < 1000; outer++)
            using (mem.BeginExecute())                                   // what a scan thread does around one program
                for (int i = 0; i < 1000; i++)
                {
                    mem.WriteInt32(d32, (i & 1) == 0 ? A : B);
                    mem.WriteInt64(d64, (i & 1) == 0 ? LA : LB);
                }

        Volatile.Write(ref stop, 1);
        foreach (var t in readers) t.Join();
        Assert.Equal(0, torn);
        Assert.Equal(1000, mem.LockHoldStats.Count);
        Assert.True(mem.LockHoldStats.Max > TimeSpan.Zero);
    }

    [Fact]
    public void Lock_hold_time_is_recorded_per_scan()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        plc.Backplane.Install(new DigitalOutputModule(1, 0, 8));
        plc.Download(new ScanCounterProgram());
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "ScanCounter") });
        plc.SetMode(CpuMode.Run);
        for (int i = 0; i < 10; i++) plc.Tick();
        Assert.Equal(10, plc.Cpu.InternalMemory.LockHoldStats.Count);
        Assert.Equal(10, plc.DataMemory.ReadInt32(ScanCounterProgram.ScanCountAddress));
    }
}

// ---- MEM-7/11/12: process image and forcing -------------------------------------------------

public class ForcingTests
{
    static (Plc plc, RecordingSink q0) Build(Action<ScanContext> program)
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        var di = new DigitalInputModule(1, Address.Parse("%IX0.0"), 8);
        di.Wire(0, new ConstSwitch(false));
        var dq = new DigitalOutputModule(2, Address.Parse("%QX0.0"), 8);
        var q0 = new RecordingSink();
        dq.Wire(0, q0);
        plc.Backplane.Install(di);
        plc.Backplane.Install(dq);
        plc.Download(new LambdaProgram("P", program));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "P") });
        return (plc, q0);
    }

    [Fact]
    public void Forced_input_is_seen_by_the_program_survives_stop_to_run_and_clearing_restores_the_physical_value()
    {
        bool seen = false;
        var (plc, _) = Build(c => seen = c.Images.Inputs.ReadBool(Addr.I00));
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.False(seen);

        plc.Cpu.Forces.SetBool(Addr.I00, true);
        plc.Tick();
        Assert.True(seen);
        Assert.True(plc.DataMemory.ReadBool(Addr.I00));                 // shared I image shows what the program saw

        plc.SetMode(CpuMode.Stop);
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.True(seen, "force must survive Stop to Run");

        plc.Cpu.Forces.Clear(Addr.I00);
        plc.Tick();
        Assert.False(seen);
    }

    [Fact]
    public void Forced_output_reaches_the_field_while_the_program_still_sees_its_own_value()
    {
        bool programView = true;
        var (plc, q0) = Build(c =>
        {
            c.Images.Outputs.WriteBool(Addr.Q00, false);
            programView = c.Images.Outputs.ReadBool(Addr.Q00);
        });
        plc.Cpu.Forces.SetBool(Addr.Q00, true);
        plc.SetMode(CpuMode.Run);
        plc.Tick();

        Assert.True(q0.Last);                                           // field
        Assert.False(programView);                                      // program
        Assert.False(plc.DataMemory.ReadBool(Addr.Q00));                // shared Q image is unforced
        plc.Cpu.Forces.Clear(Addr.Q00);
        plc.Tick();
        Assert.False(q0.Last);
    }

    [Fact]
    public void Program_sees_one_stable_input_image_per_scan()
    {
        bool a = false, b = false;
        bool physical = false;
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        var di = new DigitalInputModule(1, 0, 8);
        di.Wire(0, new ScriptedSwitch(() => physical));
        plc.Backplane.Install(di);
        plc.Download(new LambdaProgram("P", c =>
        {
            a = c.Images.Inputs.ReadBool(Addr.I00);
            physical = true;                                            // field changes mid-scan
            b = c.Images.Inputs.ReadBool(Addr.I00);
        }));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "P") });
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.Equal(a, b);
        Assert.False(b);
    }

    [Fact]
    public void ForceTable_validates_and_reports_active_transitions()
    {
        var t = new ForceTable();
        var events = new List<bool>();
        t.ActiveChanged += events.Add;

        Assert.Throws<MemoryAccessException>(() => t.SetBool(Address.Parse("%MX0.0"), true));
        Assert.Throws<ArgumentException>(() => t.Set(Address.Parse("%IW0"), new byte[] { 1 }));

        t.SetBool(Addr.I00, true);
        t.SetBool(Addr.Q00, true);
        t.SetBool(Addr.I00, false);                                     // replace, still 2 entries
        Assert.Equal(2, t.Entries.Count);
        Assert.Equal(new[] { true }, events);

        t.Clear(Addr.I00);
        t.Clear(Addr.Q00);
        Assert.False(t.Active);
        Assert.Equal(new[] { true, false }, events);

        t.SetBool(Addr.I00, true);
        t.ClearAll();
        Assert.Equal(new[] { true, false, true, false }, events);
    }

    [Fact]
    public void ProgrammingDevice_force_goes_through_the_force_table()
    {
        var (plc, _) = Build(_ => { });
        var dev = new ProgrammingDevice();
        dev.Connect(plc);
        dev.Force(Addr.I00, true);
        Assert.True(plc.Cpu.Forces.Active);
        dev.ClearForce(Addr.I00);
        Assert.False(plc.Cpu.Forces.Active);
    }
}

// ---- MEM-9/10: restart types and retain -------------------------------------------------------

public class RestartTests
{
    static Plc NewPlc()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        plc.Download(new LambdaProgram("P", _ => { }));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "P") });
        return plc;
    }

    static void Fill(Plc plc)
    {
        plc.DataMemory.WriteInt16(Addr.NonRetainW, 11);
        plc.DataMemory.WriteInt16(Addr.RetainW, 22);
        plc.DataMemory.WriteInt16(Addr.DataW, 33);
    }

    static (short nonRetain, short retain, short data) Read(Plc plc) =>
        (plc.DataMemory.ReadInt16(Addr.NonRetainW), plc.DataMemory.ReadInt16(Addr.RetainW), plc.DataMemory.ReadInt16(Addr.DataW));

    [Fact]
    public void Cold_zeroes_everything_including_retain()
    {
        var plc = NewPlc();
        Fill(plc);
        plc.SetMode(CpuMode.Run, RestartType.Cold);
        Assert.Equal(CpuMode.Run, plc.Mode);
        Assert.Equal(((short)0, (short)0, (short)0), Read(plc));
    }

    [Fact]
    public void Warm_zeroes_everything_except_retain()
    {
        var plc = NewPlc();
        Fill(plc);
        plc.SetMode(CpuMode.Run, RestartType.Warm);
        Assert.Equal(((short)0, (short)22, (short)0), Read(plc));
    }

    [Fact]
    public void Default_stop_to_run_is_warm()
    {
        var plc = NewPlc();
        Fill(plc);
        plc.SetMode(CpuMode.Run);
        Assert.Equal(((short)0, (short)22, (short)0), Read(plc));
    }

    [Fact]
    public void Hot_changes_nothing_after_a_clean_stop()
    {
        var plc = NewPlc();
        plc.SetMode(CpuMode.Run);
        plc.SetMode(CpuMode.Stop);
        Fill(plc);
        plc.SetMode(CpuMode.Run, RestartType.Hot);
        Assert.Equal(CpuMode.Run, plc.Mode);
        Assert.Equal(((short)11, (short)22, (short)33), Read(plc));
    }

    [Fact]
    public void Hot_is_refused_on_first_run_and_after_a_download()
    {
        var plc = NewPlc();
        plc.SetMode(CpuMode.Run, RestartType.Hot);
        Assert.Equal(CpuMode.Stop, plc.Mode);
        Assert.NotNull(plc.LastModeError);

        plc.SetMode(CpuMode.Run); plc.SetMode(CpuMode.Stop);
        plc.Download(new LambdaProgram("P", _ => { }));
        plc.SetMode(CpuMode.Run, RestartType.Hot);
        Assert.Equal(CpuMode.Stop, plc.Mode);
    }

    [Fact]
    public void Hot_is_refused_after_a_fault()
    {
        var plc = NewPlc();
        plc.Download(new LambdaProgram("P", _ => throw new InvalidOperationException("boom")));
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.Equal(CpuMode.Fault, plc.Mode);
        plc.SetMode(CpuMode.Stop);                                      // fault cleared, no download in between
        plc.SetMode(CpuMode.Run, RestartType.Hot);
        Assert.Equal(CpuMode.Stop, plc.Mode);
    }

    [Fact]
    public void Retain_capture_and_restore_round_trip()
    {
        var mem = new DataMemory();
        mem.WriteInt32(Address.Parse("%MD57344"), 0x1234);
        mem.WriteInt32(Address.Parse("%MD65532"), 0x5678);             // last 4 bytes of M
        var snap = mem.Capture();
        Assert.Equal(MemoryLayout.DefaultRetainBytes, snap.Length);

        mem.ApplyRestart(RestartType.Cold);
        Assert.Equal(0, mem.ReadInt32(Address.Parse("%MD57344")));
        mem.Restore(snap);
        Assert.Equal(0x1234, mem.ReadInt32(Address.Parse("%MD57344")));
        Assert.Equal(0x5678, mem.ReadInt32(Address.Parse("%MD65532")));
        Assert.Throws<ArgumentException>(() => mem.Restore(new byte[3]));
    }

    [Fact]
    public void Custom_retain_regions_in_D_are_honoured()
    {
        var layout = new MemoryLayout
        {
            RetainRegions = new[] { new RetainRegion(MemoryArea.D, 100, 16), new RetainRegion(MemoryArea.M, 0, 4) },
        };
        var mem = new DataMemory(layout);
        mem.WriteInt32(Address.Parse("%DD100"), 5);
        mem.WriteInt32(Address.Parse("%DD200"), 6);
        mem.WriteInt32(Address.Parse("%MD0"), 7);
        mem.WriteInt32(Address.Parse("%MD8"), 8);
        mem.ApplyRestart(RestartType.Warm);
        Assert.Equal(5, mem.ReadInt32(Address.Parse("%DD100")));
        Assert.Equal(0, mem.ReadInt32(Address.Parse("%DD200")));
        Assert.Equal(7, mem.ReadInt32(Address.Parse("%MD0")));
        Assert.Equal(0, mem.ReadInt32(Address.Parse("%MD8")));
        Assert.Equal(20, mem.RetainLength);
    }

    [Fact]
    public void Invalid_retain_layout_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new DataMemory(new MemoryLayout
        { RetainRegions = new[] { new RetainRegion(MemoryArea.I, 0, 4) } }));
        Assert.Throws<ArgumentException>(() => new DataMemory(new MemoryLayout
        { RetainRegions = new[] { new RetainRegion(MemoryArea.M, 0, 8), new RetainRegion(MemoryArea.M, 4, 8) } }));
    }
}

// ---- MEM-13: module mapping --------------------------------------------------------------------

public class MappingTests
{
    [Fact]
    public void Overlapping_modules_are_rejected_naming_both()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        plc.Backplane.Install(new DigitalInputModule(1, Address.Parse("%IX0.0"), 8));
        var ex = Assert.Throws<ArgumentException>(() =>
            plc.Backplane.Install(new DigitalInputModule(2, Address.Parse("%IX0.4"), 8)));
        Assert.Contains("slot 1", ex.Message);
        Assert.Contains("slot 2", ex.Message);
    }

    [Fact]
    public void Adjacent_modules_and_same_bits_in_different_areas_are_fine()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        plc.Backplane.Install(new DigitalInputModule(1, Address.Parse("%IX0.0"), 4));
        plc.Backplane.Install(new DigitalInputModule(2, Address.Parse("%IX0.4"), 4));
        plc.Backplane.Install(new DigitalOutputModule(3, Address.Parse("%QX0.0"), 8));
        plc.Backplane.Install(new AnalogInputModule(4, Address.Parse("%ID4"), 2));
        Assert.Throws<ArgumentException>(() =>
            plc.Backplane.Install(new AnalogInputModule(5, Address.Parse("%ID8"), 2)));   // bytes 8..15 overlap slot 4 (4..11)
    }

    [Fact]
    public void Module_must_use_the_right_area_and_prefix()
    {
        Assert.Throws<ArgumentException>(() => new DigitalInputModule(1, Address.Parse("%QX0.0"), 8));
        Assert.Throws<ArgumentException>(() => new DigitalOutputModule(1, Address.Parse("%QW0"), 8));
        Assert.Throws<ArgumentException>(() => new AnalogInputModule(1, Address.Parse("%IW0"), 2));
    }

    [Fact]
    public void Module_mapped_outside_its_area_blocks_Run()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        plc.Backplane.Install(new DigitalInputModule(1, Address.Parse("%IX1023.0"), 16));
        plc.Download(new LambdaProgram("P", _ => { }));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "P") });
        plc.SetMode(CpuMode.Run);
        Assert.Equal(CpuMode.Stop, plc.Mode);
        Assert.Contains("outside", plc.LastModeError);
    }

    [Fact]
    public void Analog_modules_exchange_REAL_values_through_the_images()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        var ai = new AnalogInputModule(1, Address.Parse("%ID0"), 2);
        ai.Wire(1, new ConstAnalog(12.5));
        var ao = new AnalogOutputModule(2, Address.Parse("%QD0"), 1);
        var sink = new AnalogSink();
        ao.Wire(0, sink);
        plc.Backplane.Install(ai);
        plc.Backplane.Install(ao);
        plc.Download(new LambdaProgram("P", c =>
            c.Images.Outputs.WriteSingle(Address.Parse("%QD0"), c.Images.Inputs.ReadSingle(Address.Parse("%ID4")) * 2f)));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "P") });
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.Equal(25.0, sink.Last);
        Assert.Equal(12.5f, plc.DataMemory.ReadSingle(Address.Parse("%ID4")));
    }

    sealed class ConstAnalog(double v) : IAnalogSource { public double Read() => v; }
    sealed class AnalogSink : IAnalogSink { public double Last; public void Write(double v) => Last = v; }
}
