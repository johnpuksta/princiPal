namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// Represents a single frame in the debugger call stack.
    /// </summary>
    public class StackFrameInfo
    {
        public int Index { get; set; }
        public string FunctionName { get; set; }
        public string Module { get; set; }
        public string Language { get; set; }
        public string FilePath { get; set; }
        public int Line { get; set; }
    }
}
