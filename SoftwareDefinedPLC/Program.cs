using System.Diagnostics;
using SoftwareDefinedPlc;
using SoftwareDefinedPlc.LogicPrograms;
using SoftwareDefinedPlc.Testbox;

// ---- Field side -----------------------------------------------------------
var clock = new Stopwatch();
bool Between(double a, double b) => clock.Elapsed.TotalSeconds >= a && clock.Elapsed.TotalSeconds < b;

var startButton = new ScriptedSwitch(() => Between(1.0, 1.2) || Between(5.0, 5.2));
var stopButton  = new ScriptedSwitch(() => Between(4.0, 4.2));
var motor       = new LoggingLamp("Q0 motor    ", clock);
var heartbeat   = new LoggingLamp("Q1 heartbeat", clock);   // noisy; we only count its edges

// ---- PLC assembly ---------------------------------------------------------
var plc = new Plc(new PowerSupply(), new SchedulerOptions { Log = m => Console.WriteLine($"  [runtime] {m}") });

var di = new DigitalInputModule(slot: 1, baseAddress: 0, channels: 8);
di.Wire(0, startButton);
di.Wire(1, stopButton);

var dq = new DigitalOutputModule(slot: 2, baseAddress: 0, channels: 8);
dq.Wire(0, motor);
dq.Wire(1, heartbeat);

plc.Backplane.Install(di);
plc.Backplane.Install(dq);
plc.Communications.Add(new ModbusTcpInterface());
plc.Cpu.Faulted += f => Console.WriteLine($"  FAULT {f.Reason} in {f.Source}: {f.Message}");

// ---- Programming device: download, configure tasks, go to Run -----------------
const string taskName = "Main";
var programmer = new ProgrammingDevice();
programmer.Connect(plc);
programmer.Download(new MotorStartStop());
programmer.Download(new ScanCounterProgram());
programmer.ConfigureTasks(new[]
{
    new TaskConfig(taskName, TaskKind.Cyclic, Interval: TimeSpan.FromMilliseconds(10), Priority: 0,
                   WatchdogLimit: TimeSpan.FromMilliseconds(30),
                   ProgramInstances: new[] { "MotorStartStop", "ScanCounter" }),
});

clock.Start();
programmer.SetMode(CpuMode.Run);
Console.WriteLine($"CPU mode: {plc.Mode}");
if (plc.Mode != CpuMode.Run) { Console.WriteLine($"CPU failed to enter Run: {plc.LastModeError}"); return 2; }

// ---- The runtime owns the scan now; the host only monitors -----------------------
var runFor = TimeSpan.FromSeconds(6);
var stats = plc.Cpu.Scheduler.Stats[taskName];
using (var report = new PeriodicTimer(TimeSpan.FromSeconds(1)))
{
    while (clock.Elapsed < runFor && plc.Mode == CpuMode.Run && await report.WaitForNextTickAsync())
    {
        Console.WriteLine($"M0 scans={programmer.MonitorWord(ScanCounterProgram.ScanCountAddress),5} | " +
                          $"exec last {stats.Last.TotalMicroseconds,5:F0} us max {stats.Max.TotalMicroseconds,5:F0} us | " +
                          $"jitter max {stats.JitterMax.TotalMicroseconds,6:F0} us | overruns {stats.OverrunCount} | " +
                          $"I0={programmer.MonitorBit(Address.Parse("%IX0.0")),-5} Q0={motor.State,-5} Q1={heartbeat.State}");
    }
}

var stopAt = clock.Elapsed;
var modeAtEnd = plc.Mode;
programmer.SetMode(CpuMode.Stop);        // tasks stop at a scan boundary, outputs go to fail-safe
clock.Stop();

// ---- Self-checks -----------------------------------------------------------------
var scansDone = (long)programmer.MonitorWord(ScanCounterProgram.ScanCountAddress);
var motorEdges = motor.Edges.Where(e => e.At <= stopAt).ToList();
var heartbeatEdges = heartbeat.Edges.Count(e => e.At <= stopAt);
var checks = new (string Name, bool Ok)[]
{
    ("CPU stayed in Run for the whole test",                    modeAtEnd == CpuMode.Run),
    ("program executed every scan (M0 == ScanCount)",           scansDone == stats.ScanCount),
    ("scan rate plausible (>= 500 scans in 6 s at 10 ms)",      stats.ScanCount >= 500),
    ("heartbeat toggled",                                       heartbeatEdges > 10),
    ("motor: on, off, on sequence",                             motorEdges.Select(e => e.Value).SequenceEqual(new[] { true, false, true })),
    ("motor seal-in held after start released",                 motorEdges.Count > 1 && (motorEdges[1].At - motorEdges[0].At).TotalSeconds > 2.5),
    ("outputs driven to fail-safe on Stop",                     plc.Mode == CpuMode.Stop && !motor.State && !heartbeat.State),
};

Console.WriteLine($"\nRan {stats.ScanCount} scans, exec max {stats.Max.TotalMicroseconds:F0} us, " +
                  $"jitter max {stats.JitterMax.TotalMicroseconds:F0} us, overruns {stats.OverrunCount}");
foreach (var c in checks) Console.WriteLine($"  {(c.Ok ? "PASS" : "FAIL")}  {c.Name}");
return checks.All(c => c.Ok) ? 0 : 1;
