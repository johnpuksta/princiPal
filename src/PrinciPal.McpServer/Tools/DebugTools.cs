using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PrinciPal.Contracts;
using PrinciPal.McpServer.Services;

namespace PrinciPal.McpServer.Tools;

/// <summary>
/// MCP tools that expose the cached Visual Studio debug state to AI clients
/// (Claude Code, Cursor, etc.). Each tool reads from the <see cref="DebugStateStore"/>
/// which is populated by the VSIX extension via REST.
/// </summary>
[McpServerToolType]
public class DebugTools
{
    private readonly DebugStateStore _store;

    public DebugTools(DebugStateStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "get_debug_state", ReadOnly = true)]
    [Description("Get the full current debug state from Visual Studio including locals, call stack, and current source location. Use this to understand what is happening at a breakpoint.")]
    public string GetDebugState(
        [Description("Max member expansion depth (0=flat, 2=default)")]
        int depth = 2)
    {
        var state = _store.GetCurrentState();
        if (state is null)
            throw new McpException("No debug state available. Make sure Visual Studio is stopped at a breakpoint and the PrinciPal extension is running.");

        if (!state.IsInBreakMode)
            throw new McpException("Visual Studio is not in break mode. Hit a breakpoint first.");

        var sb = new StringBuilder();

        if (state.CurrentLocation is not null)
        {
            sb.AppendLine("[loc]");
            sb.AppendLine(CompactFormatter.FormatLocation(state.CurrentLocation));
        }

        if (state.Locals.Count > 0)
        {
            sb.AppendLine("[locals]");
            CompactFormatter.FormatVariables(sb, state.Locals, 0, depth);
        }

        if (state.CallStack.Count > 0)
        {
            sb.AppendLine("[stack]");
            CompactFormatter.FormatCallStack(sb, state.CallStack);
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_locals", ReadOnly = true)]
    [Description("Get all local variables and their values at the current breakpoint in Visual Studio. Returns variable names, types, values, and nested members.")]
    public string GetLocals(
        [Description("Max member expansion depth (0=flat, 2=default)")]
        int depth = 2)
    {
        var state = GetBreakModeState();

        if (state.Locals.Count == 0)
            return "No local variables in the current scope.";

        var sb = new StringBuilder();
        sb.AppendLine("[locals]");
        CompactFormatter.FormatVariables(sb, state.Locals, 0, depth);
        return sb.ToString();
    }

    [McpServerTool(Name = "get_call_stack", ReadOnly = true)]
    [Description("Get the current call stack from Visual Studio debugger. Shows the chain of method calls that led to the current breakpoint.")]
    public string GetCallStack()
    {
        var state = GetBreakModeState();

        if (state.CallStack.Count == 0)
            return "Call stack is empty.";

        var sb = new StringBuilder();
        sb.AppendLine("[stack]");
        CompactFormatter.FormatCallStack(sb, state.CallStack);
        return sb.ToString();
    }

    [McpServerTool(Name = "get_source_context", ReadOnly = true)]
    [Description("Get the source code surrounding the current breakpoint location in Visual Studio. Shows approximately 30 lines with the current line highlighted.")]
    public string GetSourceContext()
    {
        var state = GetBreakModeState();

        if (state.CurrentLocation is null)
            throw new McpException("No source location information available.");

        var filePath = state.CurrentLocation.FilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return $"Source file not accessible: {filePath}";

        var lines = File.ReadAllLines(filePath);
        var currentLine = state.CurrentLocation.Line;
        var startLine = Math.Max(1, currentLine - 15);
        var endLine = Math.Min(lines.Length, currentLine + 15);

        var sb = new StringBuilder();
        sb.AppendLine($"## Source: {Path.GetFileName(filePath)}");
        sb.AppendLine($"**Function**: `{state.CurrentLocation.FunctionName}`");
        sb.AppendLine($"**Line {currentLine}** (showing {startLine}-{endLine})");
        sb.AppendLine();
        sb.AppendLine("```csharp");
        for (int i = startLine; i <= endLine; i++)
        {
            var prefix = i == currentLine ? ">>> " : "    ";
            sb.AppendLine($"{prefix}{i,4}: {lines[i - 1]}");
        }
        sb.AppendLine("```");

        return sb.ToString();
    }

    [McpServerTool(Name = "get_breakpoints", ReadOnly = true)]
    [Description("List all breakpoints currently set in Visual Studio, including their file locations, conditions, and enabled status.")]
    public string GetBreakpoints()
    {
        var state = _store.GetCurrentState();
        if (state is null)
            throw new McpException("No debug state available.");

        if (state.Breakpoints.Count == 0)
            return "No breakpoints are set.";

        var sb = new StringBuilder();
        sb.AppendLine("[breakpoints]");
        foreach (var bp in state.Breakpoints)
        {
            var status = bp.Enabled ? "on" : "off";
            sb.Append($"{Path.GetFileName(bp.FilePath)}:{bp.Line} ({status})");
            if (!string.IsNullOrEmpty(bp.FunctionName))
                sb.Append($" {bp.FunctionName}");
            if (!string.IsNullOrEmpty(bp.Condition))
                sb.Append($" when {bp.Condition}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_expression_result", ReadOnly = true)]
    [Description("Get the result of the last expression evaluated in the Visual Studio debugger. The VSIX extension pushes expression results after evaluation.")]
    public string GetExpressionResult(
        [Description("Max member expansion depth (0=flat, 2=default)")]
        int depth = 2)
    {
        var result = _store.GetLastExpression();
        if (result is null)
            throw new McpException("No expression result available. Evaluate an expression in Visual Studio first.");

        var sb = new StringBuilder();
        var valid = result.IsValid ? "" : " [!]";
        sb.AppendLine($"expr {result.Expression}:{result.Type}={result.Value}{valid}");

        if (result.Members.Count > 0)
        {
            CompactFormatter.FormatVariables(sb, result.Members, 1, depth);
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "explain_current_state", ReadOnly = true)]
    [Description("Get a combined view of source code context, local variables, and call stack at the current breakpoint. Ideal for asking the AI to explain what is happening.")]
    public string ExplainCurrentState()
    {
        var sb = new StringBuilder();

        try { sb.AppendLine(GetSourceContext()); }
        catch { /* source may not be available */ }

        sb.AppendLine();

        try { sb.AppendLine(GetLocals()); }
        catch { /* locals may not be available */ }

        sb.AppendLine();

        try { sb.AppendLine(GetCallStack()); }
        catch { /* call stack may not be available */ }

        var text = sb.ToString().Trim();
        if (string.IsNullOrEmpty(text))
            throw new McpException("No debug state available.");

        return text;
    }

    // ------------------------------------------------------------------
    // History tools
    // ------------------------------------------------------------------

    [McpServerTool(Name = "get_breakpoint_history", ReadOnly = true)]
    [Description("Get a summary list of all breakpoint snapshots captured during this debug session. Each entry shows the snapshot index, timestamp, source location, and local variable count. Use get_snapshot to drill into a specific snapshot.")]
    public string GetBreakpointHistory()
    {
        var history = _store.GetHistory();
        if (history.Count == 0)
            throw new McpException("No breakpoint history available. Hit some breakpoints first — each break-mode stop is recorded automatically.");

        var sb = new StringBuilder();
        var totalCaptured = _store.TotalCaptured;
        if (totalCaptured > history.Count)
            sb.AppendLine($"History ({history.Count} of {totalCaptured} captured, showing #{history[0].Index}..#{history[^1].Index})");
        else
            sb.AppendLine($"History ({history.Count} snapshots)");

        foreach (var snapshot in history)
        {
            var loc = snapshot.State.CurrentLocation;
            var file = loc != null ? Path.GetFileName(loc.FilePath) : "unknown";
            var line = loc?.Line ?? 0;
            var func = loc?.FunctionName ?? "unknown";
            var localCount = snapshot.State.Locals.Count;
            var time = snapshot.CapturedAt.ToString("HH:mm:ss.fff");

            sb.AppendLine($"#{snapshot.Index} [{time}] {func} ({file}:{line}) {localCount} locals");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_snapshot", ReadOnly = true)]
    [Description("Get the full debug state for a specific breakpoint snapshot by its index number. Returns locals, call stack, and source location captured at that breakpoint hit. Use get_breakpoint_history first to see available snapshot indices.")]
    public string GetSnapshot(
        [Description("The snapshot index number from get_breakpoint_history")]
        int index,
        [Description("Detail level: full, changes, summary (default full)")]
        string detail = "full",
        [Description("Max member expansion depth (0=flat, 2=default)")]
        int depth = 2)
    {
        var snapshot = _store.GetSnapshot(index);
        if (snapshot is null)
        {
            if (index >= 0 && index < _store.TotalCaptured)
            {
                var history = _store.GetHistory();
                var oldest = history.Count > 0 ? history[0].Index : _store.TotalCaptured;
                throw new McpException($"Snapshot #{index} was evicted (history keeps last {_store.MaxHistorySize}). Oldest available: #{oldest}.");
            }
            throw new McpException($"Snapshot #{index} not found. Use get_breakpoint_history to see available snapshots.");
        }

        var state = snapshot.State;
        var sb = new StringBuilder();
        var time = snapshot.CapturedAt.ToString("HH:mm:ss.fff");

        if (state.CurrentLocation is not null)
        {
            sb.AppendLine($"#{snapshot.Index} [{time}] {CompactFormatter.FormatLocation(state.CurrentLocation)}");
        }
        else
        {
            sb.AppendLine($"#{snapshot.Index} [{time}]");
        }

        if (state.Locals.Count > 0)
        {
            sb.AppendLine("[locals]");
            CompactFormatter.FormatVariables(sb, state.Locals, 0, depth);
        }

        if (state.CallStack.Count > 0)
        {
            sb.AppendLine("[stack]");
            CompactFormatter.FormatCallStack(sb, state.CallStack);
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "explain_execution_flow", ReadOnly = true)]
    [Description("Get all captured breakpoint snapshots formatted as an execution trace. Ideal for asking the AI to analyze how values change across multiple breakpoints and explain the overall program flow.")]
    public string ExplainExecutionFlow(
        [Description("Detail level: full=complete state, changes=delta between snapshots (default), summary=location+change names only")]
        string detail = "changes",
        [Description("Max member expansion depth (0=flat, 1=default)")]
        int depth = 1,
        [Description("Start from snapshot index (default 0)")]
        int start = 0,
        [Description("Number of snapshots to show (0=all, default 0)")]
        int count = 0)
    {
        var history = _store.GetHistory();
        if (history.Count == 0)
            throw new McpException("No breakpoint history available. Hit some breakpoints first — each break-mode stop is recorded automatically.");

        var filtered = history.Where(s => s.Index >= start).ToList();
        if (count > 0)
            filtered = filtered.Take(count).ToList();

        var sb = new StringBuilder();

        // Header
        var totalCaptured = _store.TotalCaptured;
        var hasEviction = totalCaptured > history.Count;
        var hasPagination = count > 0 || start > 0;
        if (hasEviction || hasPagination)
        {
            var actualStart = filtered.Count > 0 ? filtered[0].Index : start;
            var totalLabel = hasEviction ? $"{history.Count} of {totalCaptured} captured" : $"{history.Count} total";
            sb.AppendLine($"Trace ({totalLabel}, showing {filtered.Count} from #{actualStart})");
        }
        else
            sb.AppendLine($"Trace ({filtered.Count} snapshots)");

        DebugStateSnapshot? prevSnapshot = null;

        foreach (var snapshot in filtered)
        {
            var state = snapshot.State;
            var loc = state.CurrentLocation;
            var file = loc != null ? Path.GetFileName(loc.FilePath) : "unknown";
            var func = loc?.FunctionName ?? "unknown";
            var line = loc?.Line ?? 0;
            var time = snapshot.CapturedAt.ToString("HH:mm:ss.fff");

            sb.AppendLine($"#{snapshot.Index} [{time}] {func} ({file}:{line})");

            switch (detail)
            {
                case "summary":
                    if (prevSnapshot != null)
                    {
                        var summary = CompactFormatter.FormatVariableChangeSummary(
                            prevSnapshot.State.Locals, state.Locals);
                        sb.AppendLine(summary);
                    }
                    break;

                case "full":
                    if (state.Locals.Count > 0)
                    {
                        sb.AppendLine("[locals]");
                        CompactFormatter.FormatVariables(sb, state.Locals, 0, depth);
                    }
                    if (state.CallStack.Count > 0)
                    {
                        sb.AppendLine("[stack]");
                        CompactFormatter.FormatCallStack(sb, state.CallStack);
                    }
                    break;

                case "changes":
                default:
                    if (prevSnapshot == null)
                    {
                        // First snapshot: show full state
                        if (state.Locals.Count > 0)
                        {
                            sb.AppendLine("[locals]");
                            CompactFormatter.FormatVariables(sb, state.Locals, 0, depth);
                        }
                        if (state.CallStack.Count > 0)
                        {
                            sb.AppendLine("[stack]");
                            CompactFormatter.FormatCallStack(sb, state.CallStack);
                        }
                    }
                    else
                    {
                        // Subsequent: show diff
                        CompactFormatter.FormatVariableDiff(sb,
                            prevSnapshot.State.Locals, state.Locals, depth);
                        CompactFormatter.FormatCallStackDiff(sb,
                            prevSnapshot.State.CallStack, state.CallStack);
                    }
                    break;
            }

            sb.AppendLine();
            prevSnapshot = snapshot;
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private DebugState GetBreakModeState()
    {
        var state = _store.GetCurrentState();
        if (state is null || !state.IsInBreakMode)
            throw new McpException("No debug state available or not in break mode.");
        return state;
    }
}
