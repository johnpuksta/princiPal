using System.Collections.Generic;

namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// Result of evaluating an expression in the debugger.
    /// </summary>
    public class ExpressionResult
    {
        public string Expression { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }
        public bool IsValid { get; set; }
        public List<LocalVariable> Members { get; set; } = new List<LocalVariable>();
    }
}
