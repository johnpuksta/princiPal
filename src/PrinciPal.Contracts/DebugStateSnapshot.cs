using System;

namespace PrinciPal.Contracts
{
    /// <summary>
    /// A timestamped snapshot of debug state captured when a breakpoint is hit.
    /// Stored in the history list so AI agents can review multiple breakpoint hits.
    /// </summary>
    public class DebugStateSnapshot
    {
        public int Index { get; set; }
        public DateTime CapturedAt { get; set; }
        public DebugState State { get; set; }
    }
}
