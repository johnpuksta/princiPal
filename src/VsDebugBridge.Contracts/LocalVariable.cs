using System.Collections.Generic;

namespace VsDebugBridge.Contracts
{
    /// <summary>
    /// Represents a local variable in the current debug scope, including nested members.
    /// </summary>
    public class LocalVariable
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }
        public bool IsValidValue { get; set; }
        public List<LocalVariable> Members { get; set; } = new List<LocalVariable>();
    }
}
