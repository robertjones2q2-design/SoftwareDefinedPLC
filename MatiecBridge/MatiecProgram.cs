public sealed class MatiecProgram : ILogicProgram {
    public string Name { get; } public ProgramLanguage Language => ProgramLanguage.ST;
    public void Execute(IDataMemory i, IDataMemory q, IDataMemory m) {
        CopyBitsToNative(i);            // input image  -> plc_inputs()
        plc_scan(_tick++);              // run compiled logic
        CopyBitsFromNative(q);          // plc_outputs() -> output image
    }
}