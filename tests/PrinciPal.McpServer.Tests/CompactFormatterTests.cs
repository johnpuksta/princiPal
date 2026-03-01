using System.Text;
using PrinciPal.Contracts;
using PrinciPal.McpServer.Tools;

namespace PrinciPal.McpServer.Tests;

public class CompactFormatterTests
{
    // =================================================================
    // FormatVariables
    // =================================================================

    [Fact]
    public void FormatVariables_FlatVariable_CompactFormat()
    {
        var sb = new StringBuilder();
        var vars = new List<LocalVariable>
        {
            new() { Name = "count", Type = "int", Value = "42", IsValidValue = true }
        };

        CompactFormatter.FormatVariables(sb, vars, 0, 2);

        Assert.Equal("count:int=42\r\n", sb.ToString());
    }

    [Fact]
    public void FormatVariables_NestedMembers_IndentedWithDotPrefix()
    {
        var sb = new StringBuilder();
        var vars = new List<LocalVariable>
        {
            new()
            {
                Name = "person", Type = "Person", Value = "{Person}", IsValidValue = true,
                Members = new List<LocalVariable>
                {
                    new() { Name = "Name", Type = "string", Value = "\"Alice\"", IsValidValue = true },
                    new() { Name = "Age", Type = "int", Value = "30", IsValidValue = true }
                }
            }
        };

        CompactFormatter.FormatVariables(sb, vars, 0, 2);

        var result = sb.ToString();
        Assert.Contains("person:Person={Person}", result);
        Assert.Contains("  .Name:string=\"Alice\"", result);
        Assert.Contains("  .Age:int=30", result);
    }

    [Fact]
    public void FormatVariables_InvalidValue_ShowsBangMarker()
    {
        var sb = new StringBuilder();
        var vars = new List<LocalVariable>
        {
            new() { Name = "broken", Type = "object", Value = "<error>", IsValidValue = false }
        };

        CompactFormatter.FormatVariables(sb, vars, 0, 2);

        Assert.Contains("[!]", sb.ToString());
    }

    [Fact]
    public void FormatVariables_DepthZero_ShowsDepthLimitMarker()
    {
        var sb = new StringBuilder();
        var vars = new List<LocalVariable>
        {
            new()
            {
                Name = "obj", Type = "Obj", Value = "{Obj}", IsValidValue = true,
                Members = new List<LocalVariable>
                {
                    new() { Name = "A", Type = "int", Value = "1", IsValidValue = true },
                    new() { Name = "B", Type = "int", Value = "2", IsValidValue = true }
                }
            }
        };

        CompactFormatter.FormatVariables(sb, vars, 0, 0);

        var result = sb.ToString();
        Assert.Contains("[+2]", result);
        Assert.DoesNotContain(".A", result);
    }

    [Fact]
    public void FormatVariables_DepthLimit_StopsExpansion()
    {
        var sb = new StringBuilder();
        var vars = new List<LocalVariable>
        {
            new()
            {
                Name = "root", Type = "Root", Value = "{Root}", IsValidValue = true,
                Members = new List<LocalVariable>
                {
                    new()
                    {
                        Name = "Child", Type = "Child", Value = "{Child}", IsValidValue = true,
                        Members = new List<LocalVariable>
                        {
                            new() { Name = "Deep", Type = "int", Value = "99", IsValidValue = true }
                        }
                    }
                }
            }
        };

        CompactFormatter.FormatVariables(sb, vars, 0, 1);

        var result = sb.ToString();
        Assert.Contains(".Child:Child={Child} [+1]", result);
        Assert.DoesNotContain("Deep", result);
    }

    // =================================================================
    // FormatCallStack
    // =================================================================

    [Fact]
    public void FormatCallStack_UserFrames_OneLinePerFrame()
    {
        var sb = new StringBuilder();
        var frames = new List<StackFrameInfo>
        {
            new() { Index = 0, FunctionName = "Inner", FilePath = @"C:\src\Foo.cs", Line = 15 },
            new() { Index = 1, FunctionName = "Outer", FilePath = @"C:\src\Bar.cs", Line = 30 }
        };

        CompactFormatter.FormatCallStack(sb, frames);

        var result = sb.ToString();
        Assert.Contains("0: Inner (Foo.cs:15)", result);
        Assert.Contains("1: Outer (Bar.cs:30)", result);
    }

    [Fact]
    public void FormatCallStack_ExternalCode_ShowsExtMarker()
    {
        var sb = new StringBuilder();
        var frames = new List<StackFrameInfo>
        {
            new() { Index = 0, FunctionName = "ExternalCall", FilePath = "", Line = 0 }
        };

        CompactFormatter.FormatCallStack(sb, frames);

        Assert.Contains("0: ExternalCall [ext]", sb.ToString());
    }

    // =================================================================
    // FormatCallStackFiltered
    // =================================================================

    [Fact]
    public void FormatCallStackFiltered_CollapsesFrameworkFrames()
    {
        var sb = new StringBuilder();
        var frames = new List<StackFrameInfo>
        {
            new() { Index = 0, FunctionName = "MyMethod", Module = "App.dll", FilePath = @"C:\src\App.cs", Line = 10 },
            new() { Index = 1, FunctionName = "InvokeHandler", Module = "System.Runtime.dll", FilePath = "", Line = 0 },
            new() { Index = 2, FunctionName = "HostMain", Module = "Microsoft.Hosting.dll", FilePath = "", Line = 0 },
            new() { Index = 3, FunctionName = "EntryPoint", Module = "App.dll", FilePath = @"C:\src\Entry.cs", Line = 1 }
        };

        CompactFormatter.FormatCallStackFiltered(sb, frames);

        var result = sb.ToString();
        Assert.Contains("0: MyMethod (App.cs:10)", result);
        Assert.Contains("... 2 framework frames", result);
        Assert.Contains("3: EntryPoint (Entry.cs:1)", result);
        Assert.DoesNotContain("InvokeHandler", result);
    }

    // =================================================================
    // FormatLocation
    // =================================================================

    [Fact]
    public void FormatLocation_ReturnsOneLiner()
    {
        var loc = new SourceLocation
        {
            FilePath = @"C:\project\Service.cs",
            Line = 25,
            FunctionName = "ProcessData",
            ProjectName = "MyApp"
        };

        var result = CompactFormatter.FormatLocation(loc);

        Assert.Equal("@ ProcessData (Service.cs:25) [MyApp]", result);
    }

    // =================================================================
    // FormatVariableDiff
    // =================================================================

    [Fact]
    public void FormatVariableDiff_UnchangedVariables_NoOutput()
    {
        var sb = new StringBuilder();
        var vars = new List<LocalVariable>
        {
            new() { Name = "x", Type = "int", Value = "10", IsValidValue = true }
        };

        CompactFormatter.FormatVariableDiff(sb, vars, vars, 2);

        Assert.Equal("", sb.ToString());
    }

    [Fact]
    public void FormatVariableDiff_ChangedValue_ShowsWasAnnotation()
    {
        var sb = new StringBuilder();
        var prev = new List<LocalVariable>
        {
            new() { Name = "x", Type = "int", Value = "10", IsValidValue = true }
        };
        var curr = new List<LocalVariable>
        {
            new() { Name = "x", Type = "int", Value = "20", IsValidValue = true }
        };

        CompactFormatter.FormatVariableDiff(sb, prev, curr, 2);

        var result = sb.ToString();
        Assert.Contains("[changed]", result);
        Assert.Contains("x:int=20 (was 10)", result);
    }

    [Fact]
    public void FormatVariableDiff_NewVariable_ShowsNewSection()
    {
        var sb = new StringBuilder();
        var prev = new List<LocalVariable>();
        var curr = new List<LocalVariable>
        {
            new() { Name = "y", Type = "bool", Value = "true", IsValidValue = true }
        };

        CompactFormatter.FormatVariableDiff(sb, prev, curr, 2);

        var result = sb.ToString();
        Assert.Contains("[new]", result);
        Assert.Contains("y:bool=true", result);
    }

    [Fact]
    public void FormatVariableDiff_RemovedVariable_ShowsRemovedSection()
    {
        var sb = new StringBuilder();
        var prev = new List<LocalVariable>
        {
            new() { Name = "old", Type = "string", Value = "\"gone\"", IsValidValue = true }
        };
        var curr = new List<LocalVariable>();

        CompactFormatter.FormatVariableDiff(sb, prev, curr, 2);

        var result = sb.ToString();
        Assert.Contains("[removed]", result);
        Assert.Contains("old:string", result);
    }

    [Fact]
    public void FormatVariableDiff_NestedMemberChange_ShowsMemberDiff()
    {
        var sb = new StringBuilder();
        var prev = new List<LocalVariable>
        {
            new()
            {
                Name = "obj", Type = "Obj", Value = "{Obj}", IsValidValue = true,
                Members = new List<LocalVariable>
                {
                    new() { Name = "Count", Type = "int", Value = "1", IsValidValue = true }
                }
            }
        };
        var curr = new List<LocalVariable>
        {
            new()
            {
                Name = "obj", Type = "Obj", Value = "{Obj}", IsValidValue = true,
                Members = new List<LocalVariable>
                {
                    new() { Name = "Count", Type = "int", Value = "5", IsValidValue = true }
                }
            }
        };

        CompactFormatter.FormatVariableDiff(sb, prev, curr, 2);

        var result = sb.ToString();
        Assert.Contains("[changed]", result);
        Assert.Contains(".Count:int=5 (was 1)", result);
    }

    // =================================================================
    // FormatCallStackDiff
    // =================================================================

    [Fact]
    public void FormatCallStackDiff_IdenticalStacks_ShowsUnchanged()
    {
        var sb = new StringBuilder();
        var frames = new List<StackFrameInfo>
        {
            new() { Index = 0, FunctionName = "Foo", FilePath = @"C:\a.cs", Line = 1 }
        };

        CompactFormatter.FormatCallStackDiff(sb, frames, frames);

        Assert.Contains("[stack unchanged]", sb.ToString());
    }

    [Fact]
    public void FormatCallStackDiff_DifferentStacks_ShowsNewStack()
    {
        var sb = new StringBuilder();
        var prev = new List<StackFrameInfo>
        {
            new() { Index = 0, FunctionName = "Foo", FilePath = @"C:\a.cs", Line = 1 }
        };
        var curr = new List<StackFrameInfo>
        {
            new() { Index = 0, FunctionName = "Bar", FilePath = @"C:\b.cs", Line = 5 }
        };

        CompactFormatter.FormatCallStackDiff(sb, prev, curr);

        var result = sb.ToString();
        Assert.Contains("[stack]", result);
        Assert.Contains("0: Bar (b.cs:5)", result);
    }

    // =================================================================
    // FormatVariableChangeSummary
    // =================================================================

    [Fact]
    public void FormatVariableChangeSummary_NoChanges_ReturnsNoChanges()
    {
        var vars = new List<LocalVariable>
        {
            new() { Name = "x", Type = "int", Value = "1", IsValidValue = true }
        };

        var result = CompactFormatter.FormatVariableChangeSummary(vars, vars);

        Assert.Equal("[no changes]", result);
    }

    [Fact]
    public void FormatVariableChangeSummary_MixedChanges_ReturnsNamesOnly()
    {
        var prev = new List<LocalVariable>
        {
            new() { Name = "x", Type = "int", Value = "1", IsValidValue = true },
            new() { Name = "old", Type = "int", Value = "0", IsValidValue = true }
        };
        var curr = new List<LocalVariable>
        {
            new() { Name = "x", Type = "int", Value = "2", IsValidValue = true },
            new() { Name = "y", Type = "bool", Value = "true", IsValidValue = true }
        };

        var result = CompactFormatter.FormatVariableChangeSummary(prev, curr);

        Assert.Contains("changed: x", result);
        Assert.Contains("new: y", result);
        Assert.Contains("removed: old", result);
    }

    // =================================================================
    // IsFrameworkFrame
    // =================================================================

    [Fact]
    public void IsFrameworkFrame_SystemModule_ReturnsTrue()
    {
        var frame = new StackFrameInfo { Module = "System.Runtime.dll", FilePath = "some.cs" };
        Assert.True(CompactFormatter.IsFrameworkFrame(frame));
    }

    [Fact]
    public void IsFrameworkFrame_MicrosoftModule_ReturnsTrue()
    {
        var frame = new StackFrameInfo { Module = "Microsoft.Extensions.dll", FilePath = "some.cs" };
        Assert.True(CompactFormatter.IsFrameworkFrame(frame));
    }

    [Fact]
    public void IsFrameworkFrame_EmptyFilePath_ReturnsTrue()
    {
        var frame = new StackFrameInfo { Module = "App.dll", FilePath = "" };
        Assert.True(CompactFormatter.IsFrameworkFrame(frame));
    }

    [Fact]
    public void IsFrameworkFrame_UserFrame_ReturnsFalse()
    {
        var frame = new StackFrameInfo { Module = "App.dll", FilePath = @"C:\src\App.cs" };
        Assert.False(CompactFormatter.IsFrameworkFrame(frame));
    }

    // =================================================================
    // VariablesEqual
    // =================================================================

    [Fact]
    public void VariablesEqual_SameValues_ReturnsTrue()
    {
        var a = new LocalVariable { Name = "x", Type = "int", Value = "1", IsValidValue = true };
        var b = new LocalVariable { Name = "x", Type = "int", Value = "1", IsValidValue = true };

        Assert.True(CompactFormatter.VariablesEqual(a, b, 2));
    }

    [Fact]
    public void VariablesEqual_DifferentValues_ReturnsFalse()
    {
        var a = new LocalVariable { Name = "x", Type = "int", Value = "1", IsValidValue = true };
        var b = new LocalVariable { Name = "x", Type = "int", Value = "2", IsValidValue = true };

        Assert.False(CompactFormatter.VariablesEqual(a, b, 2));
    }
}
