using System.Diagnostics;

namespace SoftwareDefinedPlc.Testbox
{
    /// <summary>Sensor whose value is a function of elapsed time (scripted pushbutton etc).</summary>
    public sealed class ScriptedSwitch : IDigitalSource
    {
        private readonly Func<bool> _value;
        public ScriptedSwitch(Func<bool> value) => _value = value;
        public bool Read() => _value();
    }

    /// <summary>Actuator that records every edge so the testbox can assert on behaviour.</summary>
    public sealed class LoggingLamp : IDigitalSink
    {
        private readonly string _name;
        private readonly Stopwatch _clock;
        public List<(TimeSpan At, bool Value)> Edges { get; } = new();
        public bool State { get; private set; }

        public LoggingLamp(string name, Stopwatch clock) { _name = name; _clock = clock; }

        public void Write(bool value)
        {
            if (value == State) return;
            State = value;
            Edges.Add((_clock.Elapsed, value));
            Console.WriteLine($"  [{_clock.Elapsed.TotalSeconds,6:F2}s] {_name} -> {(value ? "ON " : "OFF")}");
        }
    }
}
