using System.Collections.Generic;

namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// The main container for all debug state read from Visual Studio.
    /// </summary>
    public class DebugState
    {
        public bool IsInBreakMode { get; set; }
        public SourceLocation CurrentLocation { get; set; }
        public List<LocalVariable> Locals { get; set; } = new List<LocalVariable>();
        public List<StackFrameInfo> CallStack { get; set; } = new List<StackFrameInfo>();
        public List<BreakpointInfo> Breakpoints { get; set; } = new List<BreakpointInfo>();
    }
}
