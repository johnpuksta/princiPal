namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// Represents a breakpoint set in the Visual Studio debugger.
    /// </summary>
    public class BreakpointInfo
    {
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string FunctionName { get; set; }
        public bool Enabled { get; set; }
        public string Condition { get; set; }
    }
}
