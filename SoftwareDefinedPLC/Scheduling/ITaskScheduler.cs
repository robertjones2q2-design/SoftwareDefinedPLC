namespace SoftwareDefinedPlc.Scheduling
{
    public interface ITaskScheduler
    {
        void Configure(IReadOnlyList<TaskConfig> tasks);
        void Start();
        void Stop();
        IReadOnlyDictionary<string, ScanStats> Stats { get; }
        event Action<FaultInfo> Faulted;

        /// <summary>Run one scan of an <see cref="TaskKind.Event"/> task.</summary>
        void Trigger(string taskName);
    }
}
