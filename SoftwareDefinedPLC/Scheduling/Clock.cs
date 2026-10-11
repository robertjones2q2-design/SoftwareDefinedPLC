using System.Diagnostics;

namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>Stopwatch-timestamp helpers. Everything on the scan path uses raw timestamps (no allocation).</summary>
    internal static class Clock
    {
        public static readonly long Frequency = Stopwatch.Frequency;
        public static readonly long PerMillisecond = Math.Max(1, Frequency / 1000);
        private static readonly double TimeSpanTicksPerSw = (double)TimeSpan.TicksPerSecond / Frequency;

        public static long Now => Stopwatch.GetTimestamp();
        public static long ToSw(TimeSpan t) => (long)(t.Ticks / TimeSpanTicksPerSw);
        public static long ToTimeSpanTicks(long sw) => (long)(sw * TimeSpanTicksPerSw);
        public static TimeSpan ToTimeSpan(long sw) => TimeSpan.FromTicks(ToTimeSpanTicks(sw));
    }
}
