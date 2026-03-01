using System.Collections.Generic;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;

namespace PrinciPal.VsExtension.Abstractions
{
    /// <summary>
    /// Reads debugger state from the IDE. All methods must be called on the UI thread.
    /// </summary>
    public interface IDebuggerReader
    {
        bool IsInBreakMode { get; }
        bool IsDebuggingVisualStudio();
        Result<SourceLocation> ReadCurrentLocation();
        Result<List<LocalVariable>> ReadLocals(int maxDepth = 2);
        Result<List<StackFrameInfo>> ReadCallStack(int maxFrames = 20);
        Result<List<BreakpointInfo>> ReadBreakpoints();
    }
}
