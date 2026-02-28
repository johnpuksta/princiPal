using System.Collections.Generic;

namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// Source code context around the current breakpoint location.
    /// </summary>
    public class SourceContext
    {
        public string FilePath { get; set; }
        public int CurrentLine { get; set; }
        public string FunctionName { get; set; }
        public List<SourceLine> Lines { get; set; } = new List<SourceLine>();
    }

    /// <summary>
    /// A single line of source code with metadata.
    /// </summary>
    public class SourceLine
    {
        public int LineNumber { get; set; }
        public string Text { get; set; }
        public bool IsCurrentLine { get; set; }
    }
}
