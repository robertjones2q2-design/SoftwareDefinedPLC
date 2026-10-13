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
        private volatile bool _hotValid;

        public Cpu(IPowerSupply power, IBackplane backplane, IProgramMemory programMemory,
                   IReadOnlyList<ICommunicationInterface> comms, SchedulerOptions? options = null,
                   MemoryLayout? layout = null)
        {
            _power = power; _backplane = backplane; _programs = programMemory;
            _options = options ?? new SchedulerOptions();

            InternalMemory = new DataMemory(layout);
            Forces = new ForceTable(InternalMemory.Layout);
            _housekeeping = new HousekeepingWorker(comms, InternalMemory, _options.Log);
            Scheduler = new PlcTaskScheduler(new ScanEnvironment
            {
                Programs = programMemory, Backplane = backplane,
                Memory = InternalMemory, Forces = Forces, Housekeeping = _housekeeping,
            }, _options);

            Scheduler.Faulted += ReportFault;
            State.Changed += (from, to, trigger) => ModeChanged?.Invoke(from, to, trigger);
        }

        /// <summary>Shared memory: areas I and Q (the images as last published by a scan), M and D. Addressed by area.</summary>
        public DataMemory InternalMemory { get; }
        /// <summary>MEM-11/12. Survives scans, Stop to Run and every restart type.</summary>
        public ForceTable Forces { get; }
        /// <summary>MEM-9/10. Restart type used when Run is requested without an explicit one. Warm: retain kept, rest zeroed.</summary>
        public RestartType RunRestart { get; set; } = RestartType.Warm;
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
            foreach (var m in _backplane.Modules)
            {
                var r = m.ImageRange;
                if (r.StartBit < 0 || r.EndBit > InternalMemory.Layout.AreaBytes(r.Area) * 8)
                { reason = $"Module in slot {m.Slot} maps {r}, outside the {r.Area} area."; return false; }
            }
            var problems = Scheduler.Validate();
            if (problems.Count > 0) { reason = string.Join(" ", problems); return false; }
            return true;
        }

        /// <summary>Stop -> Run if the self-check passes. A failed check leaves the CPU in Stop and explains why.</summary>
        public bool TryEnterRun(out string? reason) => TryEnterRun(RunRestart, out reason);

        /// <summary>Hot restart is refused unless the last exit from Run was a clean Stop and no project was downloaded since.</summary>
        public bool TryEnterRun(RestartType restart, out string? reason)
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
                if (!PrepareMemory(restart, out reason)) return false;
                State.Transition(CpuMode.Run, "Operator command");
                _hotValid = false;
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
            lock (_modeLock) { if (State.TryTransition(CpuMode.Stop, trigger)) _hotValid = true; }   // a fault that raced us wins
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
            _hotValid = false;
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

        /// <summary>A new project invalidates hot restart (memory layout may differ).</summary>
        public void NotifyProjectChanged() => _hotValid = false;

        // ------------------------------------------------------------ internals

        private bool PrepareMemory(RestartType restart, out string? reason)
        {
            reason = null;
            if (restart == RestartType.Hot && !_hotValid)
            {
                reason = "Hot restart refused: memory was not preserved by a clean Stop (first run, fault, or new project).";
                return false;
            }
            try { InternalMemory.ApplyRestart(restart); return true; }
            catch (MemoryAccessException ex) { reason = ex.Message; return false; }
        }

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
