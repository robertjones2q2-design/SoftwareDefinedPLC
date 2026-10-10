using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Backplane
{
    public sealed class Backplane : IBackplane
    {
        private readonly List<IIoModule> _modules = new();
        public IReadOnlyList<IIoModule> Modules => _modules;
        public void Install(IIoModule module) => _modules.Add(module);
    }
}