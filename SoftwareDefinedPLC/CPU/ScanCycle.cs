using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftwareDefinedPlc.Core
{
    public sealed class ScanCycle : IScanCycle
    {
        private readonly Cpu _cpu;
        public TimeSpan LastScanTime { get; private set; }
 
        public ScanCycle(Cpu cpu) => _cpu = cpu;
 
        public void RunOnce()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _cpu.ReadInputs();       // 1. inputs  -> input image
            _cpu.ExecuteProgram();   // 2. logic   -> output image
            _cpu.WriteOutputs();     // 3. output image -> field
            _cpu.Housekeeping();     // 4. comms, diagnostics
            LastScanTime = sw.Elapsed;
        }
    }
}