namespace SoftwareDefinedPlc.Bus
{
    public sealed class Backplane : IBackplane
    {
        private readonly List<IIoModule> _modules = new();
        public IReadOnlyList<IIoModule> Modules => _modules;

        /// <summary>MEM-13: rejects a module whose image range overlaps one already installed, naming both.</summary>
        public void Install(IIoModule module)
        {
            var range = module.ImageRange;
            foreach (var other in _modules)
                if (other.ImageRange.Overlaps(range))
                    throw new ArgumentException(
                        $"Module in slot {module.Slot} ({range}) overlaps module in slot {other.Slot} ({other.ImageRange}).");
            _modules.Add(module);
        }
    }
}
