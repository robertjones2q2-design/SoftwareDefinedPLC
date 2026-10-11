namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// Per-task cycle statistics (SCAN-10). Written only by the task thread; readers get
    /// individually-atomic values, which is enough for monitoring.
    /// </summary>
    public sealed class ScanStats
    {
        private const double Alpha = 0.05;
        private long _last, _max, _jitterMax, _overruns, _count;
        private long _min = long.MaxValue;
        private double _avg;

        public TimeSpan Last      => TimeSpan.FromTicks(Volatile.Read(ref _last));
        public TimeSpan Min       => Volatile.Read(ref _count) == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Volatile.Read(ref _min));
        public TimeSpan Max       => TimeSpan.FromTicks(Volatile.Read(ref _max));
        public TimeSpan Average   => TimeSpan.FromTicks((long)Volatile.Read(ref _avg));
        public TimeSpan JitterMax => TimeSpan.FromTicks(Volatile.Read(ref _jitterMax));
        public long OverrunCount  => Volatile.Read(ref _overruns);
        public long ScanCount     => Volatile.Read(ref _count);

        internal void Record(long execTicks, long jitterTicks)
        {
            Volatile.Write(ref _last, execTicks);
            if (execTicks < _min) Volatile.Write(ref _min, execTicks);
            if (execTicks > _max) Volatile.Write(ref _max, execTicks);
            if (jitterTicks > _jitterMax) Volatile.Write(ref _jitterMax, jitterTicks);
            long c = _count + 1;
            Volatile.Write(ref _avg, c == 1 ? execTicks : _avg + Alpha * (execTicks - _avg));
            Volatile.Write(ref _count, c);
        }

        internal void RecordOverrun() => Interlocked.Increment(ref _overruns);

        /// <summary>Internal on purpose: only the engineering protocol (via the scheduler) may reset (SCAN-10).</summary>
        internal void Reset()
        {
            Volatile.Write(ref _count, 0);
            Volatile.Write(ref _last, 0);
            Volatile.Write(ref _min, long.MaxValue);
            Volatile.Write(ref _max, 0);
            Volatile.Write(ref _jitterMax, 0);
            Volatile.Write(ref _overruns, 0);
            Volatile.Write(ref _avg, 0.0);
        }
    }
}
