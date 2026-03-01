using System.Text;
using PrinciPal.Contracts;

namespace PrinciPal.McpServer.Tools;

/// <summary>
/// Token-efficient formatting for debug state output.
/// Replaces verbose markdown with compact text: <c>name:type=value</c>,
/// one-line stack frames, and diff-based execution traces.
/// </summary>
public static class CompactFormatter
{
    /// <summary>
    /// Formats variables in compact <c>name:type=value</c> format.
    /// Members are indented with <c>.</c> prefix.
    /// </summary>
    public static void FormatVariables(StringBuilder sb, List<LocalVariable> variables, int indent, int maxDepth)
    {
        var prefix = new string(' ', indent * 2);
        foreach (var v in variables)
        {
            var invalid = v.IsValidValue ? "" : " [!]";
            var depthMarker = "";
            if (maxDepth <= 0 && v.Members.Count > 0)
                depthMarker = $" [+{v.Members.Count}]";

            sb.AppendLine($"{prefix}{(indent > 0 ? "." : "")}{v.Name}:{v.Type}={v.Value}{invalid}{depthMarker}");

            if (v.Members.Count > 0 && maxDepth > 0)
            {
                FormatVariables(sb, v.Members, indent + 1, maxDepth - 1);
            }
        }
    }

    /// <summary>
    /// Formats call stack with one line per frame: <c>0: FuncName (File.cs:42)</c>.
    /// All frames are included.
    /// </summary>
    public static void FormatCallStack(StringBuilder sb, List<StackFrameInfo> frames)
    {
        foreach (var frame in frames)
        {
            var location = !string.IsNullOrEmpty(frame.FilePath)
                ? $"({System.IO.Path.GetFileName(frame.FilePath)}:{frame.Line})"
                : "[ext]";
            sb.AppendLine($"{frame.Index}: {frame.FunctionName} {location}");
        }
    }

    /// <summary>
    /// Formats call stack, collapsing consecutive framework frames
    /// (System.*, Microsoft.*, or empty FilePath) into <c>... N framework frames</c>.
    /// </summary>
    public static void FormatCallStackFiltered(StringBuilder sb, List<StackFrameInfo> frames)
    {
        int fwCount = 0;
        foreach (var frame in frames)
        {
            if (IsFrameworkFrame(frame))
            {
                fwCount++;
                continue;
            }

            if (fwCount > 0)
            {
                sb.AppendLine($"... {fwCount} framework frame{(fwCount > 1 ? "s" : "")}");
                fwCount = 0;
            }

            var location = !string.IsNullOrEmpty(frame.FilePath)
                ? $"({System.IO.Path.GetFileName(frame.FilePath)}:{frame.Line})"
                : "[ext]";
            sb.AppendLine($"{frame.Index}: {frame.FunctionName} {location}");
        }

        if (fwCount > 0)
            sb.AppendLine($"... {fwCount} framework frame{(fwCount > 1 ? "s" : "")}");
    }

    /// <summary>
    /// Single-line location: <c>@ FuncName (File.cs:42) [Project]</c>
    /// </summary>
    public static string FormatLocation(SourceLocation loc)
    {
        var file = System.IO.Path.GetFileName(loc.FilePath);
        return $"@ {loc.FunctionName} ({file}:{loc.Line}) [{loc.ProjectName}]";
    }

    /// <summary>
    /// Diff between two variable lists: [changed], [new], [removed] sections.
    /// </summary>
    public static void FormatVariableDiff(StringBuilder sb, List<LocalVariable> prev, List<LocalVariable> curr, int maxDepth)
    {
        var prevMap = BuildVariableMap(prev);
        var currMap = BuildVariableMap(curr);

        var changed = new List<string>();
        var newVars = new List<string>();
        var removed = new List<string>();

        foreach (var kvp in currMap)
        {
            if (prevMap.TryGetValue(kvp.Key, out var prevVar))
            {
                if (!VariablesEqual(prevVar, kvp.Value, maxDepth))
                {
                    changed.Add(FormatChangedVariable(prevVar, kvp.Value, maxDepth));
                }
            }
            else
            {
                var varSb = new StringBuilder();
                FormatVariables(varSb, new List<LocalVariable> { kvp.Value }, 0, maxDepth);
                newVars.Add(varSb.ToString().TrimEnd());
            }
        }

        foreach (var kvp in prevMap)
        {
            if (!currMap.ContainsKey(kvp.Key))
            {
                removed.Add($"{kvp.Key}:{kvp.Value.Type}");
            }
        }

        if (changed.Count > 0)
        {
            sb.AppendLine("[changed]");
            foreach (var c in changed) sb.AppendLine(c);
        }

        if (newVars.Count > 0)
        {
            sb.AppendLine("[new]");
            foreach (var n in newVars) sb.AppendLine(n);
        }

        if (removed.Count > 0)
        {
            sb.AppendLine("[removed]");
            foreach (var r in removed) sb.AppendLine(r);
        }
    }

    /// <summary>
    /// Diff call stacks: prints [stack unchanged] or the new stack.
    /// </summary>
    public static void FormatCallStackDiff(StringBuilder sb, List<StackFrameInfo> prev, List<StackFrameInfo> curr)
    {
        if (CallStacksEqual(prev, curr))
        {
            sb.AppendLine("[stack unchanged]");
            return;
        }

        sb.AppendLine("[stack]");
        FormatCallStack(sb, curr);
    }

    /// <summary>
    /// Names-only summary of changes for <c>detail=summary</c>.
    /// Returns e.g. <c>changed: x, y | new: z</c> or <c>[no changes]</c>.
    /// </summary>
    public static string FormatVariableChangeSummary(List<LocalVariable> prev, List<LocalVariable> curr)
    {
        var prevMap = BuildVariableMap(prev);
        var currMap = BuildVariableMap(curr);

        var changed = new List<string>();
        var newVars = new List<string>();
        var removed = new List<string>();

        foreach (var kvp in currMap)
        {
            if (prevMap.TryGetValue(kvp.Key, out var prevVar))
            {
                if (!VariablesEqual(prevVar, kvp.Value, 2))
                    changed.Add(kvp.Key);
            }
            else
            {
                newVars.Add(kvp.Key);
            }
        }

        foreach (var kvp in prevMap)
        {
            if (!currMap.ContainsKey(kvp.Key))
                removed.Add(kvp.Key);
        }

        var parts = new List<string>();
        if (changed.Count > 0) parts.Add($"changed: {string.Join(", ", changed)}");
        if (newVars.Count > 0) parts.Add($"new: {string.Join(", ", newVars)}");
        if (removed.Count > 0) parts.Add($"removed: {string.Join(", ", removed)}");

        return parts.Count > 0 ? string.Join(" | ", parts) : "[no changes]";
    }

    /// <summary>
    /// Recursive equality check for two variables including members up to maxDepth.
    /// </summary>
    public static bool VariablesEqual(LocalVariable a, LocalVariable b, int maxDepth)
    {
        if (a.Name != b.Name || a.Type != b.Type || a.Value != b.Value || a.IsValidValue != b.IsValidValue)
            return false;

        if (maxDepth <= 0)
            return true;

        if (a.Members.Count != b.Members.Count)
            return false;

        for (int i = 0; i < a.Members.Count; i++)
        {
            if (!VariablesEqual(a.Members[i], b.Members[i], maxDepth - 1))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if the frame's Module starts with System. or Microsoft.,
    /// or if its FilePath is empty.
    /// </summary>
    public static bool IsFrameworkFrame(StackFrameInfo frame)
    {
        if (string.IsNullOrEmpty(frame.FilePath))
            return true;

        var module = frame.Module ?? "";
        return module.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            || module.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static Dictionary<string, LocalVariable> BuildVariableMap(List<LocalVariable> vars)
    {
        var map = new Dictionary<string, LocalVariable>();
        foreach (var v in vars)
            map[v.Name] = v;
        return map;
    }

    private static string FormatChangedVariable(LocalVariable prev, LocalVariable curr, int maxDepth)
    {
        var sb = new StringBuilder();
        var invalid = curr.IsValidValue ? "" : " [!]";

        if (prev.Value != curr.Value)
        {
            sb.Append($"{curr.Name}:{curr.Type}={curr.Value}{invalid} (was {prev.Value})");
        }
        else
        {
            // Value same but members changed
            sb.Append($"{curr.Name}:{curr.Type}={curr.Value}{invalid}");
        }

        // Show member diffs if depth allows
        if (maxDepth > 0 && (prev.Members.Count > 0 || curr.Members.Count > 0))
        {
            var prevMembers = BuildVariableMap(prev.Members);
            var currMembers = BuildVariableMap(curr.Members);

            foreach (var kvp in currMembers)
            {
                if (prevMembers.TryGetValue(kvp.Key, out var prevMember))
                {
                    if (!VariablesEqual(prevMember, kvp.Value, maxDepth - 1))
                    {
                        var mInvalid = kvp.Value.IsValidValue ? "" : " [!]";
                        sb.AppendLine();
                        sb.Append($"  .{kvp.Value.Name}:{kvp.Value.Type}={kvp.Value.Value}{mInvalid} (was {prevMember.Value})");
                    }
                }
                else
                {
                    var mInvalid = kvp.Value.IsValidValue ? "" : " [!]";
                    sb.AppendLine();
                    sb.Append($"  .{kvp.Value.Name}:{kvp.Value.Type}={kvp.Value.Value}{mInvalid} [new]");
                }
            }

            foreach (var kvp in prevMembers)
            {
                if (!currMembers.ContainsKey(kvp.Key))
                {
                    sb.AppendLine();
                    sb.Append($"  .{kvp.Key} [removed]");
                }
            }
        }

        return sb.ToString();
    }

    private static bool CallStacksEqual(List<StackFrameInfo> a, List<StackFrameInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].FunctionName != b[i].FunctionName
                || a[i].FilePath != b[i].FilePath
                || a[i].Line != b[i].Line)
                return false;
        }
        return true;
    }
}
