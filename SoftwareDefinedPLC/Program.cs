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
var plc = new Plc(new PowerSupply());

var di = new DigitalInputModule(slot: 1, baseAddress: 0, channels: 8);
di.Wire(0, startButton);
di.Wire(1, stopButton);

var dq = new DigitalOutputModule(slot: 2, baseAddress: 0, channels: 8);
dq.Wire(0, motor);
dq.Wire(1, heartbeat);

plc.Backplane.Install(di);
plc.Backplane.Install(dq);
plc.Communications.Add(new ModbusTcpInterface());

// ---- Programming device: download, go to Run, monitor -------------------------
var programmer = new ProgrammingDevice();
programmer.Connect(plc);
programmer.Download(new CompositeProgram("Testbox", new MotorStartStop(), new ScanCounterProgram()));
programmer.SetMode(CpuMode.Run);
Console.WriteLine($"CPU mode: {plc.Mode}");
if (plc.Mode != CpuMode.Run) { Console.WriteLine("CPU failed self-check."); return 2; }

// ---- Host loop: 10 ms scan, 6 s run --------------------------------------------
const int scanMs = 10;
var runFor = TimeSpan.FromSeconds(6);
long ticks = 0;
var nextReport = TimeSpan.Zero;
var maxScan = TimeSpan.Zero;

clock.Start();
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(scanMs));
while (clock.Elapsed < runFor && await timer.WaitForNextTickAsync())
{
    plc.Tick();
    ticks++;
    if (plc.Cpu.Scan.LastScanTime > maxScan) maxScan = plc.Cpu.Scan.LastScanTime;

    if (clock.Elapsed >= nextReport)
    {
        nextReport += TimeSpan.FromSeconds(1);
        var scans = programmer.MonitorWord(ScanCounterProgram.ScanCountAddress);
        Console.WriteLine($"tick {ticks,5} | M0 scans={scans,5} | last scan {plc.Cpu.Scan.LastScanTime.TotalMicroseconds,6:F0} us | " +
                          $"I0={programmer.MonitorBit(new Address(0)),-5} Q0={motor.State,-5} Q1={heartbeat.State}");
    }
}
clock.Stop();

// ---- Self-checks -----------------------------------------------------------------
var scansDone = (long)programmer.MonitorWord(ScanCounterProgram.ScanCountAddress);
var motorEdges = motor.Edges.Select(e => e.Value).ToArray();
var checks = new (string Name, bool Ok)[]
{
    ("program executed every tick (M0 == host ticks)", scansDone == ticks),
    ("heartbeat toggled",                              heartbeat.Edges.Count > 10),
    ("motor: on, off, on sequence",                    motorEdges.SequenceEqual(new[] { true, false, true })),
    ("motor seal-in held after start released",        motor.Edges.Count > 1 && (motor.Edges[1].At - motor.Edges[0].At).TotalSeconds > 2.5),
};

Console.WriteLine($"\nRan {ticks} ticks, max scan time {maxScan.TotalMicroseconds:F0} us");
foreach (var c in checks) Console.WriteLine($"  {(c.Ok ? "PASS" : "FAIL")}  {c.Name}");
return checks.All(c => c.Ok) ? 0 : 1;
