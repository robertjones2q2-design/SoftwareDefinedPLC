namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// Cooperative, priority-ordered scan token (SCAN-11). Only one task scans at a time, so a
    /// higher-priority task never interrupts a program mid-execute, and when several tasks are
    /// due together the lowest priority number goes first.
    /// </summary>
    internal sealed class ScanGate
    {
        private readonly object _lock = new();
        private readonly int[] _waiting = new int[TaskConfig.MaxPriority + 1];
        private bool _busy;
        private bool _aborted;

        /// <returns>false if the gate was aborted while waiting.</returns>
        public bool Enter(int priority)
        {
            lock (_lock)
            {
                _waiting[priority]++;
                try
                {
                    while (!_aborted && (_busy || HigherWaiting(priority)))
                        Monitor.Wait(_lock);
                    if (_aborted) return false;
                    _busy = true;
                    return true;
                }
                finally { _waiting[priority]--; }
            }
        }

        public void Exit() { lock (_lock) { _busy = false; Monitor.PulseAll(_lock); } }

        public void Abort() { lock (_lock) { _aborted = true; Monitor.PulseAll(_lock); } }

        private bool HigherWaiting(int priority)
        {
            for (int p = 0; p < priority; p++) if (_waiting[p] > 0) return true;
            return false;
        }
    }
}
