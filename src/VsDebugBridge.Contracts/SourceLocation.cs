namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// Represents the current source location where the debugger is paused.
    /// </summary>
    public class SourceLocation
    {
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string FunctionName { get; set; }
        public string ProjectName { get; set; }
    }
}
