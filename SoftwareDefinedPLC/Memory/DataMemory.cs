using System.Diagnostics;

namespace SoftwareDefinedPlc.Memory;

/// <summary>
/// One area's bytes with no locking. Owned by a single task thread (the task-private input/output
/// copies of MEM-7). Serves only its own area; Spec 1 legacy bit indexes are re-targeted to it.
/// </summary>
public sealed class ImageBuffer : TypedMemory
{
    private readonly byte[] _data;

    public ImageBuffer(MemoryArea area, int size) { Area = area; _data = new byte[size]; }

    public MemoryArea Area { get; }
    public Span<byte> Bytes => _data;
    public void Clear() => Array.Clear(_data);
    public void CopyFrom(ImageBuffer other) => other._data.AsSpan().CopyTo(_data);

    protected override int AreaSize(MemoryArea area) => area == Area ? _data.Length : -1;
    protected override void ReadCore(Address a, Span<byte> dest) => CopyOut(_data, a, dest);
    protected override void WriteCore(Address a, ReadOnlySpan<byte> src) => CopyIn(_data, a, src);
    protected override Address ResolveLegacy(Address a) { var area = Area; return a with { Area = area }; }
}

/// <summary>MEM-8: how long scan threads held the M/D write locks during program execution.</summary>
public sealed class LockHoldStats
{
    private long _count, _total, _last, _max;

    public long Count => Volatile.Read(ref _count);
    public TimeSpan Last => ToTimeSpan(Volatile.Read(ref _last));
    public TimeSpan Max => ToTimeSpan(Volatile.Read(ref _max));
    public TimeSpan Average => Count == 0 ? TimeSpan.Zero : ToTimeSpan(Volatile.Read(ref _total) / Count);

    internal void Record(long swTicks)
    {
        Volatile.Write(ref _last, swTicks);
        long cur = Volatile.Read(ref _max);
        while (swTicks > cur)
        {
            long prev = Interlocked.CompareExchange(ref _max, swTicks, cur);
            if (prev == cur) break;
            cur = prev;
        }
        Interlocked.Add(ref _total, swTicks);
        Interlocked.Increment(ref _count);
    }

    public void Reset()
    {
        Volatile.Write(ref _count, 0); Volatile.Write(ref _total, 0);
        Volatile.Write(ref _last, 0); Volatile.Write(ref _max, 0);
    }

    private static TimeSpan ToTimeSpan(long sw) =>
        TimeSpan.FromTicks((long)(sw * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
}

/// <summary>
/// The PLC's shared memory: areas I, Q, M and D (MEM-1), each a byte array behind its own
/// ReaderWriterLockSlim (MEM-8).
/// <list type="bullet">
/// <item>A scan thread calls <see cref="BeginExecute"/> around each program execute; it then holds the M and D
/// write locks, so its own accesses are direct and external readers see a consistent snapshot per request.</item>
/// <item>Every other access takes the area lock for the duration of that single call (shared for reads).</item>
/// <item>Waiting longer than <see cref="LockTimeout"/> throws <see cref="MemoryAccessException"/> instead of hanging
/// (for example while an abandoned, hung program still holds the locks).</item>
/// </list>
/// </summary>
public sealed class DataMemory : TypedMemory, IRetainStore
{
    private sealed class Store
    {
        public readonly byte[] Data;
        public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.NoRecursion);
        public Store(int size) => Data = new byte[size];
    }

    private readonly Store[] _stores = new Store[4];
    private readonly RetainRegion[] _retain;
    private int _timeoutMs = 2000;

    public DataMemory(MemoryLayout? layout = null)
    {
        Layout = layout ?? new MemoryLayout();
        Layout.Validate();
        foreach (var area in Enum.GetValues<MemoryArea>())
            _stores[(int)area] = new Store(Layout.AreaBytes(area));
        _retain = Layout.EffectiveRetainRegions.ToArray();
        RetainLength = _retain.Sum(r => r.ByteLength);
    }

    public MemoryLayout Layout { get; }
    public int RetainLength { get; }
    public LockHoldStats LockHoldStats { get; } = new();

    public TimeSpan LockTimeout
    {
        get => TimeSpan.FromMilliseconds(_timeoutMs);
        set => _timeoutMs = (int)Math.Clamp(value.TotalMilliseconds, 1, int.MaxValue);
    }

    protected override int AreaSize(MemoryArea area) => _stores[(int)area].Data.Length;

    // ------------------------------------------------------------ per-call locking

    protected override void ReadCore(Address a, Span<byte> dest)
    {
        var s = _stores[(int)a.Area];
        if (s.Lock.IsWriteLockHeld) { CopyOut(s.Data, a, dest); return; }      // we are the executing scan thread
        if (!s.Lock.TryEnterReadLock(_timeoutMs)) throw Timeout(a);
        try { CopyOut(s.Data, a, dest); }
        finally { s.Lock.ExitReadLock(); }
    }

    protected override void WriteCore(Address a, ReadOnlySpan<byte> src)
    {
        var s = _stores[(int)a.Area];
        if (s.Lock.IsWriteLockHeld) { CopyIn(s.Data, a, src); return; }
        if (!s.Lock.TryEnterWriteLock(_timeoutMs)) throw Timeout(a);
        try { CopyIn(s.Data, a, src); }
        finally { s.Lock.ExitWriteLock(); }
    }

    private static MemoryAccessException Timeout(Address a) =>
        new(a, "Timed out waiting for the memory lock (a scan is probably stuck)");

    // ------------------------------------------------------------ scan scope

    /// <summary>Held by a scan thread for one program execute. Dispose releases the locks and records the hold time.</summary>
    public readonly struct ExecuteScope : IDisposable
    {
        private readonly DataMemory? _owner;
        private readonly long _start;
        internal ExecuteScope(DataMemory owner, long start) { _owner = owner; _start = start; }
        public void Dispose() => _owner?.EndExecute(_start);
    }

    /// <summary>Takes the M then D write locks. Nested calls on the same thread are no-ops.</summary>
    public ExecuteScope BeginExecute()
    {
        var m = _stores[(int)MemoryArea.M].Lock;
        var d = _stores[(int)MemoryArea.D].Lock;
        if (m.IsWriteLockHeld) return default;
        if (!m.TryEnterWriteLock(_timeoutMs))
            throw Timeout(new Address(MemoryArea.M, SizePrefix.B, 0));
        if (!d.TryEnterWriteLock(_timeoutMs))
        {
            m.ExitWriteLock();
            throw Timeout(new Address(MemoryArea.D, SizePrefix.B, 0));
        }
        return new ExecuteScope(this, Stopwatch.GetTimestamp());
    }

    private void EndExecute(long start)
    {
        long held = Stopwatch.GetTimestamp() - start;
        _stores[(int)MemoryArea.D].Lock.ExitWriteLock();
        _stores[(int)MemoryArea.M].Lock.ExitWriteLock();
        LockHoldStats.Record(held);
    }

    // ------------------------------------------------------------ MEM-10 restart

    /// <summary>
    /// Cold: zero everything. Warm: zero everything outside the retain regions (I and Q included). Hot: no change.
    /// Takes all four write locks (I, Q, M, D) so no reader sees a half-cleared memory.
    /// </summary>
    public void ApplyRestart(RestartType type)
    {
        if (type == RestartType.Hot) return;
        var taken = new List<Store>(4);
        try
        {
            foreach (var area in Enum.GetValues<MemoryArea>())
            {
                var s = _stores[(int)area];
                if (!s.Lock.TryEnterWriteLock(_timeoutMs)) throw Timeout(new Address(area, SizePrefix.B, 0));
                taken.Add(s);
            }
            Array.Clear(_stores[(int)MemoryArea.I].Data);
            Array.Clear(_stores[(int)MemoryArea.Q].Data);
            foreach (var area in new[] { MemoryArea.M, MemoryArea.D })
            {
                if (type == RestartType.Cold) Array.Clear(_stores[(int)area].Data);
                else ClearOutsideRetain(area);
            }
        }
        finally
        {
            for (int i = taken.Count - 1; i >= 0; i--) taken[i].Lock.ExitWriteLock();
        }
    }

    private void ClearOutsideRetain(MemoryArea area)
    {
        var data = _stores[(int)area].Data;
        int pos = 0;
        foreach (var r in _retain.Where(x => x.Area == area).OrderBy(x => x.ByteOffset))
        {
            if (r.ByteOffset > pos) Array.Clear(data, pos, r.ByteOffset - pos);
            pos = r.ByteOffset + r.ByteLength;
        }
        if (pos < data.Length) Array.Clear(data, pos, data.Length - pos);
    }

    // ------------------------------------------------------------ MEM-9 retain store

    public byte[] Capture()
    {
        var result = new byte[RetainLength];
        var m = _stores[(int)MemoryArea.M];
        var d = _stores[(int)MemoryArea.D];
        bool mTaken = EnterRead(m, MemoryArea.M), dTaken = false;
        try
        {
            dTaken = EnterRead(d, MemoryArea.D);
            int pos = 0;
            foreach (var r in _retain)
            {
                _stores[(int)r.Area].Data.AsSpan(r.ByteOffset, r.ByteLength).CopyTo(result.AsSpan(pos));
                pos += r.ByteLength;
            }
        }
        finally
        {
            if (dTaken) d.Lock.ExitReadLock();
            if (mTaken) m.Lock.ExitReadLock();
        }
        return result;
    }

    public void Restore(ReadOnlySpan<byte> data)
    {
        if (data.Length != RetainLength)
            throw new ArgumentException($"Retain data is {data.Length} bytes but the layout retains {RetainLength}.", nameof(data));
        var m = _stores[(int)MemoryArea.M];
        var d = _stores[(int)MemoryArea.D];
        bool mTaken = EnterWrite(m, MemoryArea.M), dTaken = false;
        try
        {
            dTaken = EnterWrite(d, MemoryArea.D);
            int pos = 0;
            foreach (var r in _retain)
            {
                data.Slice(pos, r.ByteLength).CopyTo(_stores[(int)r.Area].Data.AsSpan(r.ByteOffset));
                pos += r.ByteLength;
            }
        }
        finally
        {
            if (dTaken) d.Lock.ExitWriteLock();
            if (mTaken) m.Lock.ExitWriteLock();
        }
    }

    private bool EnterRead(Store s, MemoryArea area)
    {
        if (s.Lock.IsWriteLockHeld) return false;
        if (!s.Lock.TryEnterReadLock(_timeoutMs)) throw Timeout(new Address(area, SizePrefix.B, 0));
        return true;
    }

    private bool EnterWrite(Store s, MemoryArea area)
    {
        if (s.Lock.IsWriteLockHeld) return false;
        if (!s.Lock.TryEnterWriteLock(_timeoutMs)) throw Timeout(new Address(area, SizePrefix.B, 0));
        return true;
    }
}
