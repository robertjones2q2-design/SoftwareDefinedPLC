namespace SoftwareDefinedPlc.Scheduling
{
    /// <summary>
    /// SCAN-12: communications/diagnostics run here, never on a task thread. A scan only calls
    /// <see cref="Signal"/>, which is a non-blocking event set. Spec 5 replaces Service() polling
    /// with real comms workers.
    /// </summary>
    public sealed class HousekeepingWorker
    {
        private readonly IReadOnlyList<ICommunicationInterface> _comms;
        private readonly IDataMemory _memory;
        private readonly Action<string>? _log;
        private readonly ManualResetEventSlim _wake = new(false);
        private Thread? _thread;
        private volatile bool _stop;
        private long _cycles, _errors;

        public HousekeepingWorker(IReadOnlyList<ICommunicationInterface> comms, IDataMemory memory, Action<string>? log = null)
        { _comms = comms; _memory = memory; _log = log; }

        public long Cycles => Volatile.Read(ref _cycles);
        public long Errors => Volatile.Read(ref _errors);

        public void Signal() => _wake.Set();

        public void Start()
        {
            if (_thread is { IsAlive: true }) return;
            _stop = false;
            _wake.Reset();
            _thread = new Thread(Loop) { IsBackground = true, Name = "plc-housekeeping", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Stop()
        {
            var t = _thread;
            if (t is null) return;
            _stop = true;
            _wake.Set();
            if (t != Thread.CurrentThread) t.Join(TimeSpan.FromSeconds(1));
            _thread = null;
        }

        private void Loop()
        {
            while (!_stop)
            {
                _wake.Wait();
                _wake.Reset();
                if (_stop) break;
                for (int i = 0; i < _comms.Count; i++)
                {
                    try { _comms[i].Service(_memory); }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _errors);
                        _log?.Invoke($"Housekeeping: {_comms[i].Protocol} failed: {ex.Message}");
                    }
                }
                Interlocked.Increment(ref _cycles);
            }
        }
    }
}
