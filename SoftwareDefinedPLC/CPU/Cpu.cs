using System.Runtime;

namespace SoftwareDefinedPlc.Core
{
    /// <summary>
    /// Coordinator: owns the images, internal memory, the state machine and the scheduler.
    /// Built first; receives the backplane, power supply and program memory directly (no Plc back-reference).
    /// </summary>
    public sealed class Cpu
    {
        private readonly IPowerSupply _power;
        private readonly IBackplane _backplane;
        private readonly IProgramMemory _programs;
        private readonly SchedulerOptions _options;
        private readonly HousekeepingWorker _housekeeping;
        private readonly object _modeLock = new();
        private GCLatencyMode? _savedGcMode;

        public Cpu(IPowerSupply power, IBackplane backplane, IProgramMemory programMemory,
                   IReadOnlyList<ICommunicationInterface> comms, SchedulerOptions? options = null)
        {
            _power = power; _backplane = backplane; _programs = programMemory;
            _options = options ?? new SchedulerOptions();

            Images = new ProcessImage();
            InternalMemory = new DataMemory();
            _housekeeping = new HousekeepingWorker(comms, InternalMemory, _options.Log);
            Scheduler = new PlcTaskScheduler(new ScanEnvironment
            {
                Programs = programMemory, Backplane = backplane,
                Images = Images, Memory = InternalMemory, Housekeeping = _housekeeping,
            }, _options);

            Scheduler.Faulted += ReportFault;
            State.Changed += (from, to, trigger) => ModeChanged?.Invoke(from, to, trigger);
        }

        public IProcessImage Images { get; }
        public IDataMemory InputImage => Images.Inputs;
        public IDataMemory OutputImage => Images.Outputs;
        public IDataMemory InternalMemory { get; }
        public CpuStateMachine State { get; } = new();
        public PlcTaskScheduler Scheduler { get; }
        public HousekeepingWorker Housekeeping => _housekeeping;

        public CpuMode Mode => State.Current;
        public FaultInfo? LastFault { get; private set; }

        public event Action<FaultInfo>? Faulted;
        /// <summary>(from, to, trigger)</summary>
        public event Action<CpuMode, CpuMode, string>? ModeChanged;

        // ------------------------------------------------------------ mode control

        /// <summary>Startup -> Stop. Spec 7 will call this after the hardware configuration has loaded.</summary>
        public void CompleteStartup() => State.Transition(CpuMode.Stop, "Startup complete");

        public bool SelfCheck() => SelfCheck(out _);

        public bool SelfCheck(out string? reason)
        {
            reason = null;
            if (!_power.IsPowerGood) { reason = "Power supply is not good."; return false; }
            foreach (var m in _backplane.Modules)
                if (m.Status != ModuleStatus.Ok) { reason = $"Module in slot {m.Slot} is {m.Status}."; return false; }
            if (_programs.Count == 0) { reason = "No project loaded."; return false; }
            var problems = Scheduler.Validate();
            if (problems.Count > 0) { reason = string.Join(" ", problems); return false; }
            return true;
        }

        /// <summary>Stop -> Run if the self-check passes. A failed check leaves the CPU in Stop and explains why.</summary>
        public bool TryEnterRun(out string? reason)
        {
            reason = null;
            lock (_modeLock)
            {
                switch (Mode)
                {
                    case CpuMode.Run: return true;
                    case CpuMode.Fault:
                    case CpuMode.Startup: throw new InvalidModeTransitionException(Mode, CpuMode.Run);
                }
                if (!SelfCheck(out reason)) return false;
                State.Transition(CpuMode.Run, "Operator command");
            }

            try
            {
                if (_options.UseSustainedLowLatencyGc)
                {
                    _savedGcMode = GCSettings.LatencyMode;
                    GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;   // SCAN-5
                }
                _housekeeping.Start();
                Scheduler.Start();
            }
            catch (Exception ex)
            {
                ReleaseRunResources();
                lock (_modeLock) State.TryTransition(CpuMode.Stop, "Run entry failed");
                reason = ex.Message;
                return false;
            }

            if (Mode != CpuMode.Run) { reason = LastFault?.Message ?? "CPU faulted while starting."; return false; }
            return true;
        }

        /// <summary>
        /// Run -> Stop (stop tasks at a scan boundary, then drive outputs to fail-safe, then leave Run).
        /// From Fault this is the operator's fault clear.
        /// </summary>
        public bool RequestStop(string trigger, out string? reason)
        {
            reason = null;
            switch (Mode)
            {
                case CpuMode.Stop: return true;
                case CpuMode.Fault: return ClearFault(out reason);
                case CpuMode.Startup: throw new InvalidModeTransitionException(CpuMode.Startup, CpuMode.Stop);
            }

            ReleaseRunResources();         // joins task threads
            ApplyOutputFailSafe();         // IO-8: before the CPU reports it has left Run
            lock (_modeLock) State.TryTransition(CpuMode.Stop, trigger);   // a fault that raced us wins
            return true;
        }

        /// <summary>Fault -> Stop, refused while the cause persists.</summary>
        public bool ClearFault(out string? reason)
        {
            reason = null;
            if (Mode != CpuMode.Fault) return true;
            if (!_power.IsPowerGood) { reason = "Cannot clear fault: power supply is still not good."; return false; }

            ReleaseRunResources();
            lock (_modeLock)
            {
                if (State.TryTransition(CpuMode.Stop, "Fault cleared")) LastFault = null;
            }
            return true;
        }

        /// <summary>
        /// Entry point for every fault source (watchdog, program error now; I/O and power in later specs).
        /// Order matters: stop tasks, fail-safe the outputs, then leave Run.
        /// </summary>
        public void ReportFault(FaultInfo info)
        {
            Scheduler.Abort();
            ApplyOutputFailSafe();
            bool entered;
            lock (_modeLock)
            {
                entered = State.TryTransition(CpuMode.Fault, info.Reason.ToString());
                if (entered) LastFault = info;
            }
            if (entered) Faulted?.Invoke(info);
        }

        /// <summary>Deterministic-mode only (see <see cref="SchedulerOptions.Deterministic"/>).</summary>
        public void Tick() => Scheduler.Tick();

        // ------------------------------------------------------------ internals

        private void ReleaseRunResources()
        {
            Scheduler.Stop();
            _housekeeping.Stop();
            if (_savedGcMode is { } m)
            {
                try { GCSettings.LatencyMode = m; } catch { }
                _savedGcMode = null;
            }
        }

        private void ApplyOutputFailSafe()
        {
            foreach (var m in _backplane.Modules.OfType<IOutputModule>())
            {
                try { m.ApplyFailSafe(); }
                catch (Exception ex) { _options.Log?.Invoke($"Fail-safe failed on slot {m.Slot}: {ex.Message}"); }
            }
        }
    }
}
