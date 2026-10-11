using System.Diagnostics;

namespace SoftwareDefinedPlc.Tests;

// ---- fixtures -----------------------------------------------------------------------

sealed class RecordingSink : IDigitalSink
{
    /// <summary>Records edges only, so steady-state writes allocate nothing.</summary>
    public readonly List<bool> Writes = new();
    private bool _last;
    public bool Last => _last;
    public void Write(bool value)
    {
        lock (Writes) { if (Writes.Count == 0 || _last != value) Writes.Add(value); _last = value; }
    }
}

sealed class ConstSwitch(bool value) : IDigitalSource { public bool Read() => value; }

/// <summary>Records what each scan saw; optionally sleeps / throws / writes Q0.</summary>
sealed class ProbeProgram(string name) : ILogicProgram
{
    public string Name => name;
    public ProgramLanguage Language => ProgramLanguage.ST;
    public readonly List<(string Task, long Scan, TimeSpan Now, TimeSpan Delta)> Seen = new();
    public readonly List<string>? Order;
    public int SleepMs;
    public bool Throw;
    public long StartedAt;
    public bool DriveQ0 = true;

    public ProbeProgram(string name, List<string> order) : this(name) { Order = order; }
    public void Execute(in ScanContext ctx)
    {
        StartedAt = Stopwatch.GetTimestamp();
        lock (Seen) Seen.Add((ctx.TaskName, ctx.ScanNumber, ctx.Now, ctx.Delta));
        Order?.Add(name);
        if (SleepMs > 0) Thread.Sleep(SleepMs);
        if (Throw) throw new InvalidOperationException("boom");
        if (DriveQ0) ctx.Images.Outputs.WriteBit(new Address(0), true);
    }
}

static class Rig
{
    public static (Plc plc, RecordingSink q0) Build(bool deterministic, params ILogicProgram[] programs)
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = deterministic });
        var dq = new DigitalOutputModule(1, 0, 8);
        var q0 = new RecordingSink();
        dq.Wire(0, q0);
        plc.Backplane.Install(new DigitalInputModule(2, 0, 8));
        plc.Backplane.Install(dq);
        foreach (var p in programs) plc.Download(p);
        return (plc, q0);
    }

    public static TaskConfig Task(string name, int ms, int prio, int wdMs, params string[] progs) =>
        new(name, TaskKind.Cyclic, TimeSpan.FromMilliseconds(ms), prio, TimeSpan.FromMilliseconds(wdMs), progs);
}

// ---- SCAN-13: state machine -------------------------------------------------------------

public class CpuStateMachineTests
{
    static readonly (CpuMode, CpuMode)[] Legal =
    {
        (CpuMode.Startup, CpuMode.Stop), (CpuMode.Stop, CpuMode.Run), (CpuMode.Run, CpuMode.Stop),
        (CpuMode.Run, CpuMode.Fault), (CpuMode.Fault, CpuMode.Stop),
    };

    static CpuStateMachine In(CpuMode mode)
    {
        var m = new CpuStateMachine();
        switch (mode)
        {
            case CpuMode.Stop: m.Transition(CpuMode.Stop, "t"); break;
            case CpuMode.Run: m.Transition(CpuMode.Stop, "t"); m.Transition(CpuMode.Run, "t"); break;
            case CpuMode.Fault: m.Transition(CpuMode.Stop, "t"); m.Transition(CpuMode.Run, "t"); m.Transition(CpuMode.Fault, "t"); break;
        }
        return m;
    }

    [Fact]
    public void Every_pair_is_legal_or_throws()
    {
        var all = Enum.GetValues<CpuMode>();
        foreach (var from in all)
            foreach (var to in all)
            {
                var m = In(from);
                if (Legal.Contains((from, to)))
                {
                    m.Transition(to, "t");
                    Assert.Equal(to, m.Current);
                }
                else
                {
                    var ex = Assert.Throws<InvalidModeTransitionException>(() => m.Transition(to, "t"));
                    Assert.Equal(from, ex.From);
                    Assert.Equal(from, m.Current);
                    Assert.False(In(from).TryTransition(to, "t"), $"{from}->{to}");
                }
            }
    }

    [Fact]
    public void Fault_to_Run_is_not_allowed()
        => Assert.Throws<InvalidModeTransitionException>(() => In(CpuMode.Fault).Transition(CpuMode.Run, "t"));
}

// ---- SCAN-6/11/12/13 and deterministic mode -----------------------------------------------

public class ScanTests
{
    [Fact]
    public void Tick_requires_deterministic_mode()
    {
        var (plc, _) = Rig.Build(false);
        Assert.Throws<InvalidOperationException>(() => plc.Tick());
    }

    [Fact]
    public void Run_is_refused_without_a_project_and_stays_in_Stop()
    {
        var (plc, _) = Rig.Build(true);
        plc.SetMode(CpuMode.Run);
        Assert.Equal(CpuMode.Stop, plc.Mode);
        Assert.True(plc.LastModeError is not null);
    }

    [Fact]
    public void Run_is_refused_when_a_task_references_an_unknown_program()
    {
        var (plc, _) = Rig.Build(true, new ProbeProgram("A"));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "Nope") });
        plc.SetMode(CpuMode.Run);
        Assert.Equal(CpuMode.Stop, plc.Mode);
    }

    [Fact]
    public void ScanContext_is_populated_with_deterministic_time()
    {
        var p = new ProbeProgram("A");
        var (plc, _) = Rig.Build(true, p);
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "A") });
        plc.SetMode(CpuMode.Run);
        for (int i = 0; i < 3; i++) plc.Tick();
        Assert.Equal(3, p.Seen.Count);
        Assert.Equal(("T", 1L, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)), p.Seen[0]);
        Assert.Equal(("T", 3L, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(10)), p.Seen[2]);
    }

    [Fact]
    public void Tasks_run_in_priority_order_and_programs_in_binding_order()
    {
        var order = new List<string>();
        var (plc, _) = Rig.Build(true,
            new ProbeProgram("lowA", order), new ProbeProgram("lowB", order), new ProbeProgram("high", order));
        plc.ConfigureTasks(new[]
        {
            Rig.Task("Slow", 10, 5, 0, "lowA", "lowB"),   // configured first, but lower priority
            Rig.Task("Fast", 10, 1, 0, "high"),
        });
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.Equal(new[] { "high", "lowA", "lowB" }, order.ToArray());
    }

    [Fact]
    public void Startup_task_runs_once_before_cyclic_tasks()
    {
        var order = new List<string>();
        var (plc, _) = Rig.Build(true, new ProbeProgram("init", order), new ProbeProgram("main", order));
        plc.ConfigureTasks(new[]
        {
            Rig.Task("Main", 10, 0, 0, "main"),
            new TaskConfig("Init", TaskKind.Startup, TimeSpan.Zero, 0, TimeSpan.Zero, new[] { "init" }),
        });
        plc.SetMode(CpuMode.Run);
        Assert.Equal(new[] { "init" }, order.ToArray());
        plc.Tick(); plc.Tick();
        Assert.Equal(new[] { "init", "main", "main" }, order.ToArray());
    }

    [Fact]
    public void Program_exception_faults_the_cpu_and_drives_failsafe()
    {
        var p = new ProbeProgram("A") { Throw = true };
        var (plc, q0) = Rig.Build(true, p);
        FaultInfo? fault = null;
        plc.Cpu.Faulted += f => fault = f;
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "A") });
        plc.SetMode(CpuMode.Run);
        plc.Tick();
        Assert.Equal(CpuMode.Fault, plc.Mode);
        Assert.Equal(FaultReason.ProgramRuntimeError, fault!.Reason);
        Assert.Equal("T/A", fault.Source);
        Assert.False(q0.Last);
    }

    [Fact]
    public void Fault_can_only_be_left_through_Stop()
    {
        var p = new ProbeProgram("A") { Throw = true };
        var (plc, _) = Rig.Build(true, p);
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "A") });
        plc.SetMode(CpuMode.Run); plc.Tick();
        Assert.Equal(CpuMode.Fault, plc.Mode);
        Assert.Throws<InvalidModeTransitionException>(() => plc.Cpu.TryEnterRun(out _));
        plc.SetMode(CpuMode.Stop);
        Assert.Equal(CpuMode.Stop, plc.Mode);
        Assert.True(plc.Cpu.LastFault is null);
    }

    [Fact]
    public void Run_to_Stop_applies_failsafe_before_reporting_Stop()
    {
        var p = new ProbeProgram("A");
        var (plc, q0) = Rig.Build(true, p);
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "A") });
        plc.SetMode(CpuMode.Run); plc.Tick();
        Assert.True(q0.Last);
        bool outputOffWhenStopReported = false;
        plc.Cpu.ModeChanged += (_, to, _) => { if (to == CpuMode.Stop) outputOffWhenStopReported = !q0.Last; };
        plc.SetMode(CpuMode.Stop);
        Assert.True(outputOffWhenStopReported);
    }

    // SCAN-5: the scan path must not allocate in steady state.
    [Fact]
    public void Steady_state_scan_allocates_nothing_over_100k_scans()
    {
        var (plc, _) = Rig.Build(true, new MotorStartStop(), new ScanCounterProgram());
        ((DigitalInputModule)plc.Backplane.Modules.OfType<DigitalInputModule>().First()).Wire(0, new ConstSwitch(true));
        plc.ConfigureTasks(new[] { Rig.Task("Main", 10, 0, 0, "MotorStartStop", "ScanCounter") });
        plc.SetMode(CpuMode.Run);
        for (int i = 0; i < 2000; i++) plc.Tick();      // warm up: JIT, dictionary growth

        long gen0 = GC.CollectionCount(0);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) plc.Tick();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;

        Assert.Equal(0L, allocated);
        Assert.Equal(gen0, (long)GC.CollectionCount(0));
        Assert.Equal(102_000L, plc.Cpu.Scheduler.Stats["Main"].ScanCount);
    }
}

// ---- SCAN-7/8/9/10 with real threads -------------------------------------------------------

public class ThreadedSchedulerTests
{
    static bool WaitFor(Func<bool> cond, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(1); }
        return cond();
    }

    [Fact]
    public void Cyclic_task_runs_at_its_rate_and_stats_are_filled()
    {
        var p = new ProbeProgram("A");
        var (plc, _) = Rig.Build(false, p);
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "A") });
        plc.SetMode(CpuMode.Run);
        Thread.Sleep(500);
        plc.SetMode(CpuMode.Stop);
        var st = plc.Cpu.Scheduler.Stats["T"];
        Assert.InRange(st.ScanCount, 35L, 55L);                 // ~50, absolute deadlines: no accumulated drift
        Assert.Equal(st.ScanCount, (long)p.Seen.Count);
        Assert.True(st.Max >= st.Min && st.Average > TimeSpan.Zero);
        Assert.Equal(CpuMode.Stop, plc.Mode);
    }

    [Fact]
    public void Watchdog_faults_within_40ms_of_scan_start_and_drives_failsafe()
    {
        var p = new ProbeProgram("Slow") { SleepMs = 50 };
        var (plc, q0) = Rig.Build(false, p);
        long faultedAt = 0;
        FaultInfo? fault = null;
        plc.Cpu.Faulted += f => { fault = f; faultedAt = Stopwatch.GetTimestamp(); };
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 30, "Slow") });
        plc.SetMode(CpuMode.Run);

        Assert.True(WaitFor(() => fault is not null, 1000), "no fault");
        var elapsed = Stopwatch.GetElapsedTime(p.StartedAt, faultedAt);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(40), $"fault took {elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(FaultReason.WatchdogTimeout, fault!.Reason);
        Assert.Equal("T", fault.Source);
        Assert.Equal(CpuMode.Fault, plc.Mode);
        Thread.Sleep(80);                                        // the hung scan finishes; it must not re-energise Q0
        Assert.False(q0.Last);
        plc.SetMode(CpuMode.Stop);
        Assert.Equal(CpuMode.Stop, plc.Mode);
    }

    [Fact]
    public void Overrun_is_counted_and_missed_periods_are_skipped()
    {
        var p = new ProbeProgram("Slow") { SleepMs = 25 };       // > interval, < watchdog
        var (plc, _) = Rig.Build(false, p);
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 200, "Slow") });
        plc.SetMode(CpuMode.Run);
        Thread.Sleep(400);
        plc.SetMode(CpuMode.Stop);
        var st = plc.Cpu.Scheduler.Stats["T"];
        Assert.True(st.OverrunCount > 3, $"overruns {st.OverrunCount}");
        Assert.True(st.ScanCount < 20, $"scans {st.ScanCount}");  // 400 ms / 25 ms; a burst would be ~40
        Assert.Equal(CpuMode.Stop, plc.Mode);
    }

    // SCAN-11: when several tasks are due together the lowest priority number scans first, and nobody preempts mid-scan.
    [Fact]
    public void Gate_admits_waiters_in_priority_order()
    {
        var gate = new ScanGate();
        Assert.True(gate.Enter(15));                      // a scan is in progress
        var order = new List<int>();
        var threads = new List<Thread>();
        foreach (var prio in new[] { 9, 3, 20, 1 })
        {
            var p = prio;
            var t = new Thread(() => { gate.Enter(p); lock (order) order.Add(p); gate.Exit(); });
            t.Start(); threads.Add(t);
        }
        Thread.Sleep(150);                                // let all four queue up behind the running scan
        Assert.Empty(order);                     // nobody got in mid-scan
        gate.Exit();
        foreach (var t in threads) t.Join();
        Assert.Equal(new[] { 1, 3, 9, 20 }, order.ToArray());
    }

    [Fact]
    public void Aborted_gate_releases_waiters()
    {
        var gate = new ScanGate();
        gate.Enter(0);
        bool? entered = null;
        var t = new Thread(() => entered = gate.Enter(5));
        t.Start(); Thread.Sleep(50);
        gate.Abort(); t.Join();
        Assert.False(entered == true);
    }

    [Fact]
    public void Event_task_runs_only_when_triggered()
    {
        var p = new ProbeProgram("E");
        var (plc, _) = Rig.Build(false, p);
        plc.ConfigureTasks(new[] { new TaskConfig("Evt", TaskKind.Event, TimeSpan.Zero, 0, TimeSpan.Zero, new[] { "E" }) });
        plc.SetMode(CpuMode.Run);
        Thread.Sleep(100);
        Assert.Empty(p.Seen);
        plc.Cpu.Scheduler.Trigger("Evt");
        Assert.True(WaitFor(() => p.Seen.Count == 1, 1000));
        plc.SetMode(CpuMode.Stop);
    }

    [Fact]
    public void Stats_reset_only_through_the_scheduler()
    {
        var (plc, _) = Rig.Build(true, new ProbeProgram("A"));
        plc.ConfigureTasks(new[] { Rig.Task("T", 10, 0, 0, "A") });
        plc.SetMode(CpuMode.Run); plc.Tick(); plc.Tick();
        Assert.Equal(2L, plc.Cpu.Scheduler.Stats["T"].ScanCount);
        plc.Cpu.Scheduler.ResetStats();
        Assert.Equal(0L, plc.Cpu.Scheduler.Stats["T"].ScanCount);
    }
}

// ---- Acceptance 3: the existing testbox behaviour, deterministic -----------------------------

public class TestboxTests
{
    [Fact]
    public void Motor_seal_in_and_heartbeat_behave_as_before_the_migration()
    {
        var plc = new Plc(new PowerSupply(), new SchedulerOptions { Deterministic = true });
        bool start = false, stop = false;
        var di = new DigitalInputModule(1, 0, 8);
        di.Wire(0, new ScriptedSwitch(() => start));
        di.Wire(1, new ScriptedSwitch(() => stop));
        var dq = new DigitalOutputModule(2, 0, 8);
        var motor = new RecordingSink(); var hb = new RecordingSink();
        dq.Wire(0, motor); dq.Wire(1, hb);
        plc.Backplane.Install(di); plc.Backplane.Install(dq);
        plc.Download(new MotorStartStop()); plc.Download(new ScanCounterProgram());
        plc.ConfigureTasks(new[] { Rig.Task("Main", 10, 0, 0, "MotorStartStop", "ScanCounter") });
        plc.SetMode(CpuMode.Run);

        void Scans(int n) { for (int i = 0; i < n; i++) plc.Tick(); }
        Scans(5);  Assert.False(motor.Last);
        start = true; Scans(2); start = false; Scans(50);
        Assert.True(motor.Last, "seal-in must hold after start is released");
        stop = true; Scans(2); stop = false; Scans(10);
        Assert.False(motor.Last);
        start = true; Scans(2); start = false; Scans(5);
        Assert.True(motor.Last);
        Assert.Equal(76d, plc.DataMemory.ReadWord(ScanCounterProgram.ScanCountAddress));
        Assert.True(hb.Writes.Count >= 2);
    }
}
