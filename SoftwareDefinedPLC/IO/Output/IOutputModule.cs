namespace SoftwareDefinedPlc.IO.Output
{
    public interface IOutputModule : IIoModule
    {
        /// <summary>Drive field devices from the output image.</summary>
        void WriteFrom(IDataMemory outputImage);

        /// <summary>
        /// Put every channel in its safe state (IO-8). Spec 1 hard-wires GoToZero;
        /// Spec 4 makes it configurable per module/channel (HoldLast, GoToZero, Substitute).
        /// </summary>
        void ApplyFailSafe();
    }
}
