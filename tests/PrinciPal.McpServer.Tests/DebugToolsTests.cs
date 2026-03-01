using ModelContextProtocol;
using PrinciPal.Contracts;
using PrinciPal.McpServer.Services;
using PrinciPal.McpServer.Tools;

namespace PrinciPal.McpServer.Tests;

public class DebugToolsTests
{
    private readonly DebugStateStore _store = new();
    private readonly DebugTools _tools;

    public DebugToolsTests()
    {
        _tools = new DebugTools(_store);
    }

    // ---------------------------------------------------------------
    // Helper to build a typical break-mode DebugState
    // ---------------------------------------------------------------
    private static DebugState CreateBreakModeState(
        SourceLocation? location = null,
        List<LocalVariable>? locals = null,
        List<StackFrameInfo>? callStack = null,
        List<BreakpointInfo>? breakpoints = null)
    {
        return new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = location ?? new SourceLocation
            {
                FilePath = @"C:\project\Service.cs",
                Line = 25,
                Column = 1,
                FunctionName = "ProcessData",
                ProjectName = "MyApp"
            },
            Locals = locals ?? new List<LocalVariable>(),
            CallStack = callStack ?? new List<StackFrameInfo>(),
            Breakpoints = breakpoints ?? new List<BreakpointInfo>()
        };
    }

    // =================================================================
    // GetDebugState
    // =================================================================

    [Fact]
    public void GetDebugState_ThrowsMcpException_WhenNoStateAvailable()
    {
        var ex = Assert.Throws<McpException>(() => _tools.GetDebugState());

        Assert.Contains("No debug state available", ex.Message);
    }

    [Fact]
    public void GetDebugState_ThrowsMcpException_WhenNotInBreakMode()
    {
        _store.Update(new DebugState { IsInBreakMode = false });

        var ex = Assert.Throws<McpException>(() => _tools.GetDebugState());

        Assert.Contains("not in break mode", ex.Message);
    }

    [Fact]
    public void GetDebugState_ReturnsFormattedOutput_WithLocalsCallStackAndLocation()
    {
        var state = CreateBreakModeState(
            location: new SourceLocation
            {
                FilePath = @"C:\src\App.cs",
                Line = 10,
                Column = 1,
                FunctionName = "Run",
                ProjectName = "TestProject"
            },
            locals: new List<LocalVariable>
            {
                new() { Name = "count", Type = "int", Value = "42", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "Run", Module = "TestProject.dll", FilePath = @"C:\src\App.cs", Line = 10, Language = "C#" }
            });
        _store.Update(state);

        var result = _tools.GetDebugState();

        Assert.Contains("## Debug State", result);
        Assert.Contains("### Current Location", result);
        Assert.Contains(@"C:\src\App.cs", result);
        Assert.Contains("**Line**: 10", result);
        Assert.Contains("**Function**: Run", result);
        Assert.Contains("**Project**: TestProject", result);
        Assert.Contains("### Local Variables", result);
        Assert.Contains("**count** (`int`): `42`", result);
        Assert.Contains("### Call Stack", result);
        Assert.Contains("`Run`", result);
        Assert.Contains("TestProject.dll", result);
    }

    [Fact]
    public void GetDebugState_OmitsLocationSection_WhenCurrentLocationIsNull()
    {
        var state = CreateBreakModeState(location: null);
        // Set location explicitly to null after construction
        state.CurrentLocation = null;
        _store.Update(state);

        var result = _tools.GetDebugState();

        Assert.Contains("## Debug State", result);
        Assert.DoesNotContain("### Current Location", result);
    }

    // =================================================================
    // GetLocals
    // =================================================================

    [Fact]
    public void GetLocals_ReturnsFormattedVariables_WithNestedMembers()
    {
        var state = CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new()
                {
                    Name = "person",
                    Type = "Person",
                    Value = "{Person}",
                    IsValidValue = true,
                    Members = new List<LocalVariable>
                    {
                        new() { Name = "Name", Type = "string", Value = "\"Alice\"", IsValidValue = true },
                        new() { Name = "Age", Type = "int", Value = "30", IsValidValue = true }
                    }
                }
            });
        _store.Update(state);

        var result = _tools.GetLocals();

        Assert.Contains("## Local Variables", result);
        Assert.Contains("**person** (`Person`): `{Person}`", result);
        Assert.Contains("**Name** (`string`): `\"Alice\"`", result);
        Assert.Contains("**Age** (`int`): `30`", result);
    }

    [Fact]
    public void GetLocals_ShowsEmptyMessage_WhenNoLocals()
    {
        _store.Update(CreateBreakModeState(locals: new List<LocalVariable>()));

        var result = _tools.GetLocals();

        Assert.Equal("No local variables in the current scope.", result);
    }

    [Fact]
    public void GetLocals_ThrowsMcpException_WhenNoState()
    {
        var ex = Assert.Throws<McpException>(() => _tools.GetLocals());

        Assert.Contains("No debug state available", ex.Message);
    }

    [Fact]
    public void GetLocals_MarksInvalidValues()
    {
        var state = CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new() { Name = "broken", Type = "object", Value = "<error>", IsValidValue = false }
            });
        _store.Update(state);

        var result = _tools.GetLocals();

        Assert.Contains("(could not evaluate)", result);
    }

    // =================================================================
    // GetCallStack
    // =================================================================

    [Fact]
    public void GetCallStack_ReturnsFormattedStackFrames()
    {
        var state = CreateBreakModeState(
            callStack: new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "Inner", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Foo.cs", Line = 15 },
                new() { Index = 1, FunctionName = "Outer", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Bar.cs", Line = 30 },
                new() { Index = 2, FunctionName = "ExternalCall", Module = "System.dll", Language = "C#", FilePath = "", Line = 0 }
            });
        _store.Update(state);

        var result = _tools.GetCallStack();

        Assert.Contains("## Call Stack", result);
        Assert.Contains("0. `Inner` [C#]", result);
        Assert.Contains("Module: App.dll", result);
        Assert.Contains(@"Location: C:\src\Foo.cs:15", result);
        Assert.Contains("1. `Outer` [C#]", result);
        Assert.Contains(@"Location: C:\src\Bar.cs:30", result);
        Assert.Contains("2. `ExternalCall` [C#]", result);
        Assert.Contains("Location: (external code)", result);
    }

    [Fact]
    public void GetCallStack_ReturnsEmptyMessage_WhenNoFrames()
    {
        _store.Update(CreateBreakModeState(callStack: new List<StackFrameInfo>()));

        var result = _tools.GetCallStack();

        Assert.Equal("Call stack is empty.", result);
    }

    [Fact]
    public void GetCallStack_ThrowsMcpException_WhenNotInBreakMode()
    {
        _store.Update(new DebugState { IsInBreakMode = false });

        var ex = Assert.Throws<McpException>(() => _tools.GetCallStack());

        Assert.Contains("not in break mode", ex.Message);
    }

    // =================================================================
    // GetSourceContext
    // =================================================================

    [Fact]
    public void GetSourceContext_ThrowsMcpException_WhenNoLocation()
    {
        var state = CreateBreakModeState();
        state.CurrentLocation = null;
        _store.Update(state);

        var ex = Assert.Throws<McpException>(() => _tools.GetSourceContext());

        Assert.Contains("No source location information available", ex.Message);
    }

    [Fact]
    public void GetSourceContext_ReturnsNotAccessible_WhenFileDoesNotExist()
    {
        var state = CreateBreakModeState(
            location: new SourceLocation
            {
                FilePath = @"C:\nonexistent\path\File.cs",
                Line = 10,
                FunctionName = "Test",
                ProjectName = "Proj"
            });
        _store.Update(state);

        var result = _tools.GetSourceContext();

        Assert.Contains("Source file not accessible", result);
        Assert.Contains(@"C:\nonexistent\path\File.cs", result);
    }

    [Fact]
    public void GetSourceContext_ReturnsFormattedSource_WithHighlightedCurrentLine()
    {
        // Create a temporary file for this test
        var tempFile = Path.GetTempFileName() + ".cs";
        try
        {
            var lines = new string[30];
            for (int i = 0; i < 30; i++)
                lines[i] = $"// Line {i + 1}";
            File.WriteAllLines(tempFile, lines);

            var state = CreateBreakModeState(
                location: new SourceLocation
                {
                    FilePath = tempFile,
                    Line = 15,
                    FunctionName = "TestMethod",
                    ProjectName = "TestProject"
                });
            _store.Update(state);

            var result = _tools.GetSourceContext();

            Assert.Contains("## Source:", result);
            Assert.Contains("**Function**: `TestMethod`", result);
            Assert.Contains("**Line 15**", result);
            Assert.Contains("```csharp", result);
            // Current line should be highlighted with >>>
            Assert.Contains(">>>", result);
            Assert.Contains("// Line 15", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // =================================================================
    // GetBreakpoints
    // =================================================================

    [Fact]
    public void GetBreakpoints_ReturnsFormattedBreakpointList()
    {
        var state = CreateBreakModeState(
            breakpoints: new List<BreakpointInfo>
            {
                new()
                {
                    FilePath = @"C:\src\Controller.cs",
                    Line = 50,
                    Enabled = true,
                    FunctionName = "HandleRequest",
                    Condition = "id > 0"
                },
                new()
                {
                    FilePath = @"C:\src\Service.cs",
                    Line = 100,
                    Enabled = false,
                    FunctionName = "",
                    Condition = ""
                }
            });
        _store.Update(state);

        var result = _tools.GetBreakpoints();

        Assert.Contains("## Breakpoints", result);
        Assert.Contains("**Controller.cs:50** (enabled)", result);
        Assert.Contains("Function: `HandleRequest`", result);
        Assert.Contains("Condition: `id > 0`", result);
        Assert.Contains("**Service.cs:100** (disabled)", result);
    }

    [Fact]
    public void GetBreakpoints_ReturnsEmptyMessage_WhenNoBreakpoints()
    {
        _store.Update(CreateBreakModeState(breakpoints: new List<BreakpointInfo>()));

        var result = _tools.GetBreakpoints();

        Assert.Equal("No breakpoints are set.", result);
    }

    [Fact]
    public void GetBreakpoints_ThrowsMcpException_WhenNoState()
    {
        var ex = Assert.Throws<McpException>(() => _tools.GetBreakpoints());

        Assert.Contains("No debug state available", ex.Message);
    }

    [Fact]
    public void GetBreakpoints_DoesNotRequireBreakMode()
    {
        // GetBreakpoints checks _store.GetCurrentState() but does NOT require IsInBreakMode
        var state = new DebugState
        {
            IsInBreakMode = false,
            Breakpoints = new List<BreakpointInfo>
            {
                new() { FilePath = @"C:\src\Test.cs", Line = 1, Enabled = true }
            }
        };
        _store.Update(state);

        var result = _tools.GetBreakpoints();

        Assert.Contains("**Test.cs:1** (enabled)", result);
    }

    // =================================================================
    // GetExpressionResult
    // =================================================================

    [Fact]
    public void GetExpressionResult_ThrowsMcpException_WhenNoExpressionAvailable()
    {
        var ex = Assert.Throws<McpException>(() => _tools.GetExpressionResult());

        Assert.Contains("No expression result available", ex.Message);
    }

    [Fact]
    public void GetExpressionResult_ReturnsFormattedResult()
    {
        _store.UpdateExpression(new ExpressionResult
        {
            Expression = "list.Count",
            Value = "3",
            Type = "int",
            IsValid = true
        });

        var result = _tools.GetExpressionResult();

        Assert.Contains("## Expression Result", result);
        Assert.Contains("**Expression**: `list.Count`", result);
        Assert.Contains("**Type**: `int`", result);
        Assert.Contains("**Value**: `3`", result);
        Assert.Contains("**Valid**: True", result);
    }

    [Fact]
    public void GetExpressionResult_IncludesMembers_WhenPresent()
    {
        _store.UpdateExpression(new ExpressionResult
        {
            Expression = "myObj",
            Value = "{MyClass}",
            Type = "MyClass",
            IsValid = true,
            Members = new List<LocalVariable>
            {
                new() { Name = "Id", Type = "int", Value = "7", IsValidValue = true },
                new() { Name = "Label", Type = "string", Value = "\"test\"", IsValidValue = true }
            }
        });

        var result = _tools.GetExpressionResult();

        Assert.Contains("### Members", result);
        Assert.Contains("**Id** (`int`): `7`", result);
        Assert.Contains("**Label** (`string`): `\"test\"`", result);
    }

    // =================================================================
    // ExplainCurrentState
    // =================================================================

    [Fact]
    public void ExplainCurrentState_CombinesLocalsAndCallStack()
    {
        var state = CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new() { Name = "x", Type = "int", Value = "10", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "Calculate", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Calc.cs", Line = 5 }
            });
        // Use a non-existent file so GetSourceContext falls back gracefully
        state.CurrentLocation = new SourceLocation
        {
            FilePath = @"C:\nonexistent\Calc.cs",
            Line = 5,
            FunctionName = "Calculate",
            ProjectName = "App"
        };
        _store.Update(state);

        var result = _tools.ExplainCurrentState();

        // Source context should still contribute (returns "Source file not accessible" rather than throwing)
        Assert.Contains("Source file not accessible", result);
        // Locals should be present
        Assert.Contains("**x** (`int`): `10`", result);
        // Call stack should be present
        Assert.Contains("`Calculate`", result);
    }

    [Fact]
    public void ExplainCurrentState_ThrowsMcpException_WhenAllSectionsEmpty()
    {
        // No state at all - all three sub-calls will throw, resulting in an empty string
        var ex = Assert.Throws<McpException>(() => _tools.ExplainCurrentState());

        Assert.Contains("No debug state available", ex.Message);
    }

    // =================================================================
    // GetBreakpointHistory
    // =================================================================

    [Fact]
    public void GetBreakpointHistory_ThrowsMcpException_WhenNoHistory()
    {
        var ex = Assert.Throws<McpException>(() => _tools.GetBreakpointHistory());

        Assert.Contains("No breakpoint history available", ex.Message);
    }

    [Fact]
    public void GetBreakpointHistory_ReturnsSummary_WithMultipleSnapshots()
    {
        _store.Update(CreateBreakModeState(
            location: new SourceLocation { FilePath = @"C:\src\A.cs", Line = 10, FunctionName = "MethodA", ProjectName = "P" },
            locals: new List<LocalVariable>
            {
                new() { Name = "x", Type = "int", Value = "1", IsValidValue = true }
            }));
        _store.Update(CreateBreakModeState(
            location: new SourceLocation { FilePath = @"C:\src\B.cs", Line = 20, FunctionName = "MethodB", ProjectName = "P" },
            locals: new List<LocalVariable>
            {
                new() { Name = "a", Type = "int", Value = "1", IsValidValue = true },
                new() { Name = "b", Type = "int", Value = "2", IsValidValue = true }
            }));

        var result = _tools.GetBreakpointHistory();

        Assert.Contains("2 snapshots", result);
        Assert.Contains("#0", result);
        Assert.Contains("#1", result);
        Assert.Contains("`MethodA`", result);
        Assert.Contains("`MethodB`", result);
        Assert.Contains("A.cs:10", result);
        Assert.Contains("B.cs:20", result);
        Assert.Contains("1 locals", result);
        Assert.Contains("2 locals", result);
    }

    // =================================================================
    // GetSnapshot
    // =================================================================

    [Fact]
    public void GetSnapshot_ThrowsMcpException_WhenNotFound()
    {
        var ex = Assert.Throws<McpException>(() => _tools.GetSnapshot(999));

        Assert.Contains("Snapshot #999 not found", ex.Message);
    }

    [Fact]
    public void GetSnapshot_ReturnsFullDetail_ForValidIndex()
    {
        _store.Update(CreateBreakModeState(
            location: new SourceLocation { FilePath = @"C:\src\File.cs", Line = 42, FunctionName = "DoWork", ProjectName = "App" },
            locals: new List<LocalVariable>
            {
                new() { Name = "count", Type = "int", Value = "5", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "DoWork", Module = "App.dll", Language = "C#", FilePath = @"C:\src\File.cs", Line = 42 }
            }));

        var result = _tools.GetSnapshot(0);

        Assert.Contains("## Snapshot #0", result);
        Assert.Contains("### Location", result);
        Assert.Contains(@"C:\src\File.cs", result);
        Assert.Contains("**Function**: DoWork", result);
        Assert.Contains("### Local Variables", result);
        Assert.Contains("**count** (`int`): `5`", result);
        Assert.Contains("### Call Stack", result);
        Assert.Contains("`DoWork`", result);
    }

    // =================================================================
    // ExplainExecutionFlow
    // =================================================================

    [Fact]
    public void ExplainExecutionFlow_ThrowsMcpException_WhenNoHistory()
    {
        var ex = Assert.Throws<McpException>(() => _tools.ExplainExecutionFlow());

        Assert.Contains("No breakpoint history available", ex.Message);
    }

    [Fact]
    public void ExplainExecutionFlow_ReturnsFormattedTrace_WithMultipleSnapshots()
    {
        _store.Update(CreateBreakModeState(
            location: new SourceLocation { FilePath = @"C:\src\Start.cs", Line = 5, FunctionName = "Begin", ProjectName = "App" },
            locals: new List<LocalVariable>
            {
                new() { Name = "input", Type = "string", Value = "\"hello\"", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "Begin", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Start.cs", Line = 5 }
            }));
        _store.Update(CreateBreakModeState(
            location: new SourceLocation { FilePath = @"C:\src\Middle.cs", Line = 15, FunctionName = "Process", ProjectName = "App" },
            locals: new List<LocalVariable>
            {
                new() { Name = "input", Type = "string", Value = "\"HELLO\"", IsValidValue = true },
                new() { Name = "processed", Type = "bool", Value = "true", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "Process", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Middle.cs", Line = 15 },
                new() { Index = 1, FunctionName = "Begin", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Start.cs", Line = 6 }
            }));

        var result = _tools.ExplainExecutionFlow();

        Assert.Contains("Execution Trace (2 breakpoints captured)", result);
        Assert.Contains("Snapshot #0", result);
        Assert.Contains("Snapshot #1", result);
        Assert.Contains("`Begin()`", result);
        Assert.Contains("`Process()`", result);
        Assert.Contains("**input** (`string`): `\"hello\"`", result);
        Assert.Contains("**input** (`string`): `\"HELLO\"`", result);
        Assert.Contains("**processed** (`bool`): `true`", result);
    }

    [Fact]
    public void ExplainCurrentState_ReturnsPartialContent_WhenSomeSectionsFail()
    {
        // State with locals but no location and empty call stack
        var state = CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new() { Name = "flag", Type = "bool", Value = "true", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>());
        state.CurrentLocation = null;
        _store.Update(state);

        var result = _tools.ExplainCurrentState();

        // GetSourceContext throws (no location) - swallowed by try/catch
        // GetLocals returns content
        Assert.Contains("**flag** (`bool`): `true`", result);
        // GetCallStack returns "Call stack is empty." which is still content
        Assert.Contains("Call stack is empty.", result);
    }
}
