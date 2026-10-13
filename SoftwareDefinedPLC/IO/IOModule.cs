namespace SoftwareDefinedPlc.IO
{
    public interface IIoModule
    {
        int Slot { get; }
        int ChannelCount { get; }
        bool IsIsolated { get; }          // optocoupler / transformer isolation
        ModuleStatus Status { get; }

        /// <summary>MEM-13: the part of the I or Q image this module occupies. Used for overlap and range checks.</summary>
        ImageRange ImageRange { get; }
    }
}
