using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VsDebugBridge.Contracts;
using VsDebugBridge.McpServer.Services;

namespace VsDebugBridge.McpServer.Tools;

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
    public string GetDebugState()
    {
        var state = _store.GetCurrentState();
        if (state is null)
            throw new McpException("No debug state available. Make sure Visual Studio is stopped at a breakpoint and the VsDebugBridge extension is running.");

        if (!state.IsInBreakMode)
            throw new McpException("Visual Studio is not in break mode. Hit a breakpoint first.");

        var sb = new StringBuilder();
        sb.AppendLine("## Debug State");
        sb.AppendLine();

        // Current location
        if (state.CurrentLocation is not null)
        {
            sb.AppendLine("### Current Location");
            sb.AppendLine($"- **File**: {state.CurrentLocation.FilePath}");
            sb.AppendLine($"- **Line**: {state.CurrentLocation.Line}");
            sb.AppendLine($"- **Function**: {state.CurrentLocation.FunctionName}");
            sb.AppendLine($"- **Project**: {state.CurrentLocation.ProjectName}");
            sb.AppendLine();
        }

        // Locals
        if (state.Locals.Count > 0)
        {
            sb.AppendLine("### Local Variables");
            FormatVariables(sb, state.Locals, indent: 0);
            sb.AppendLine();
        }

        // Call stack
        if (state.CallStack.Count > 0)
        {
            sb.AppendLine("### Call Stack");
            foreach (var frame in state.CallStack)
            {
                sb.AppendLine($"  {frame.Index}. `{frame.FunctionName}` ({frame.Module}) - {frame.FilePath}:{frame.Line}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_locals", ReadOnly = true)]
    [Description("Get all local variables and their values at the current breakpoint in Visual Studio. Returns variable names, types, values, and nested members.")]
    public string GetLocals()
    {
        var state = GetBreakModeState();

        if (state.Locals.Count == 0)
            return "No local variables in the current scope.";

        var sb = new StringBuilder();
        sb.AppendLine("## Local Variables");
        sb.AppendLine();
        FormatVariables(sb, state.Locals, indent: 0);
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
        sb.AppendLine("## Call Stack");
        sb.AppendLine();
        foreach (var frame in state.CallStack)
        {
            var location = !string.IsNullOrEmpty(frame.FilePath)
                ? $"{frame.FilePath}:{frame.Line}"
                : "(external code)";
            sb.AppendLine($"  {frame.Index}. `{frame.FunctionName}` [{frame.Language}]");
            sb.AppendLine($"     Module: {frame.Module}");
            sb.AppendLine($"     Location: {location}");
        }

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
        sb.AppendLine("## Breakpoints");
        sb.AppendLine();
        foreach (var bp in state.Breakpoints)
        {
            var status = bp.Enabled ? "enabled" : "disabled";
            sb.AppendLine($"- **{Path.GetFileName(bp.FilePath)}:{bp.Line}** ({status})");
            if (!string.IsNullOrEmpty(bp.FunctionName))
                sb.AppendLine($"  Function: `{bp.FunctionName}`");
            if (!string.IsNullOrEmpty(bp.Condition))
                sb.AppendLine($"  Condition: `{bp.Condition}`");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_expression_result", ReadOnly = true)]
    [Description("Get the result of the last expression evaluated in the Visual Studio debugger. The VSIX extension pushes expression results after evaluation.")]
    public string GetExpressionResult()
    {
        var result = _store.GetLastExpression();
        if (result is null)
            throw new McpException("No expression result available. Evaluate an expression in Visual Studio first.");

        var sb = new StringBuilder();
        sb.AppendLine("## Expression Result");
        sb.AppendLine();
        sb.AppendLine($"- **Expression**: `{result.Expression}`");
        sb.AppendLine($"- **Type**: `{result.Type}`");
        sb.AppendLine($"- **Value**: `{result.Value}`");
        sb.AppendLine($"- **Valid**: {result.IsValid}");

        if (result.Members.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Members");
            FormatVariables(sb, result.Members, indent: 0);
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
    // Helpers
    // ------------------------------------------------------------------

    private DebugState GetBreakModeState()
    {
        var state = _store.GetCurrentState();
        if (state is null || !state.IsInBreakMode)
            throw new McpException("No debug state available or not in break mode.");
        return state;
    }

    private static void FormatVariables(StringBuilder sb, List<LocalVariable> variables, int indent)
    {
        var prefix = new string(' ', indent * 2);
        foreach (var v in variables)
        {
            var validMarker = v.IsValidValue ? "" : " (could not evaluate)";
            sb.AppendLine($"{prefix}- **{v.Name}** (`{v.Type}`): `{v.Value}`{validMarker}");

            if (v.Members.Count > 0 && indent < 3)
            {
                FormatVariables(sb, v.Members, indent + 1);
            }
        }
    }
}
