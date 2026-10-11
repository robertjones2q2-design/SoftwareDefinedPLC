using System.Runtime.InteropServices;

namespace SoftwareDefinedPlc.Scheduling
{
    /// <param name="RealtimePriority">SCHED_FIFO priority 1..99 (Linux only; needs CAP_SYS_NICE).</param>
    /// <param name="CpuAffinity">CPU indexes the task thread may run on (Linux only).</param>
    public sealed record TaskTuning(int? RealtimePriority = null, IReadOnlyList<int>? CpuAffinity = null);

    /// <summary>SCAN-4. Tunes the CALLING thread; returns a warning message on failure, null on success or no-op.</summary>
    public interface IThreadTuner
    {
        string? TuneCurrentThread(string taskName, TaskTuning tuning);
    }

    public sealed class NoOpThreadTuner : IThreadTuner
    {
        public string? TuneCurrentThread(string taskName, TaskTuning tuning) => null;
    }

    public static class ThreadTuners
    {
        public static IThreadTuner CreateDefault() =>
            OperatingSystem.IsLinux() ? new LinuxThreadTuner() : new NoOpThreadTuner();
    }

    public sealed class LinuxThreadTuner : IThreadTuner
    {
        private const int SCHED_FIFO = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct SchedParam { public int SchedPriority; }

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_setscheduler(int pid, int policy, ref SchedParam param);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_setaffinity(int pid, nuint cpusetsize, ulong[] mask);

        public string? TuneCurrentThread(string taskName, TaskTuning tuning)
        {
            if (!OperatingSystem.IsLinux()) return null;
            var problems = new List<string>();
            try
            {
                if (tuning.CpuAffinity is { Count: > 0 } cpus)
                {
                    var mask = new ulong[(cpus.Max() / 64) + 1];
                    foreach (var c in cpus) mask[c / 64] |= 1UL << (c % 64);
                    // pid 0 = calling thread on Linux
                    if (sched_setaffinity(0, (nuint)(mask.Length * sizeof(ulong)), mask) != 0)
                        problems.Add($"affinity failed (errno {Marshal.GetLastWin32Error()})");
                }
                if (tuning.RealtimePriority is int prio)
                {
                    var p = new SchedParam { SchedPriority = prio };
                    if (sched_setscheduler(0, SCHED_FIFO, ref p) != 0)
                        problems.Add($"SCHED_FIFO {prio} failed (errno {Marshal.GetLastWin32Error()}; needs CAP_SYS_NICE/root)");
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                problems.Add("libc scheduling calls unavailable: " + ex.Message);
            }
            return problems.Count == 0 ? null : $"Task '{taskName}': " + string.Join("; ", problems);
        }
    }

    /// <summary>Raises the Windows timer resolution to 1 ms while the scheduler runs (default is ~15.6 ms).</summary>
    internal static class PlatformTimer
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint ms);
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]   private static extern uint TimeEndPeriod(uint ms);

        public static void Begin() { if (OperatingSystem.IsWindows()) try { TimeBeginPeriod(1); } catch { } }
        public static void End()   { if (OperatingSystem.IsWindows()) try { TimeEndPeriod(1); } catch { } }
    }
}
