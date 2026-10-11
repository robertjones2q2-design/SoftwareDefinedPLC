namespace SoftwareDefinedPlc.Core
{
    /// <summary>
    /// The only place CPU mode changes (SCAN-13).
    /// Startup->Stop, Stop->Run, Run->Stop, Run->Fault, Fault->Stop. Everything else throws.
    /// </summary>
    public sealed class CpuStateMachine
    {
        private readonly object _gate = new();
        private volatile CpuMode _current = CpuMode.Startup;

        public CpuMode Current => _current;

        /// <summary>(from, to, trigger)</summary>
        public event Action<CpuMode, CpuMode, string>? Changed;

        public static bool IsAllowed(CpuMode from, CpuMode to) => (from, to) switch
        {
            (CpuMode.Startup, CpuMode.Stop) => true,
            (CpuMode.Stop,    CpuMode.Run)  => true,
            (CpuMode.Run,     CpuMode.Stop) => true,
            (CpuMode.Run,     CpuMode.Fault) => true,
            (CpuMode.Fault,   CpuMode.Stop) => true,
            _ => false,
        };

        public void Transition(CpuMode to, string trigger)
        {
            CpuMode from;
            lock (_gate)
            {
                from = _current;
                if (!IsAllowed(from, to)) throw new InvalidModeTransitionException(from, to);
                _current = to;
            }
            Changed?.Invoke(from, to, trigger);
        }

        /// <summary>For asynchronous sources (watchdog, I/O) that race with operator commands.</summary>
        public bool TryTransition(CpuMode to, string trigger)
        {
            CpuMode from;
            lock (_gate)
            {
                from = _current;
                if (!IsAllowed(from, to)) return false;
                _current = to;
            }
            Changed?.Invoke(from, to, trigger);
            return true;
        }
    }
}
