using PrinciPal.Application.Abstractions;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.Infrastructure.Services;

namespace PrinciPal.Infrastructure.Tests.Services;

public class DebugQueryServiceTests
{
    private readonly SessionManager _sessionManager = new();
    private readonly IDebugStateStore _store;
    private readonly DebugQueryService _service;

    private const string TestSessionId = "a1b2c3d4";
    private const string TestSessionName = "TestApp";

    public DebugQueryServiceTests()
    {
        _store = _sessionManager.GetOrCreateSession(TestSessionId, TestSessionName, @"C:\src\TestApp.sln");
        _service = new DebugQueryService(_sessionManager, new SourceFileReader());
    }

    #region Helpers

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

    #endregion

    #region ListSessions

    [Fact]
    public void ListSessions_ReturnsSessionInfo()
    {
        var result = _service.ListSessions();

        Assert.True(result.IsSuccess);
        Assert.Contains("1 session(s):", result.Value);
        Assert.Contains(TestSessionName, result.Value);
        Assert.Contains(TestSessionId, result.Value);
        Assert.Contains("idle", result.Value);
    }

    [Fact]
    public void ListSessions_ShowsDebugging_WhenInBreakMode()
    {
        _store.Update(CreateBreakModeState());

        var result = _service.ListSessions();

        Assert.True(result.IsSuccess);
        Assert.Contains("debugging", result.Value);
    }

    [Fact]
    public void ListSessions_ShowsMultipleSessions()
    {
        _sessionManager.GetOrCreateSession("e5f6a7b8", "OtherApp", @"C:\src\OtherApp.sln");

        var result = _service.ListSessions();

        Assert.True(result.IsSuccess);
        Assert.Contains("2 session(s):", result.Value);
        Assert.Contains(TestSessionName, result.Value);
        Assert.Contains("OtherApp", result.Value);

        // Cleanup
        _sessionManager.RemoveSession("e5f6a7b8");
    }

    #endregion

    #region Session resolution

    [Fact]
    public void ExplicitSession_ByName_Works_WithMultipleSessions()
    {
        _store.Update(CreateBreakModeState());
        _sessionManager.GetOrCreateSession("e5f6a7b8", "OtherApp", @"C:\src\OtherApp.sln");

        // Query by unique name
        var result = _service.GetDebugState(session: TestSessionName);

        Assert.True(result.IsSuccess);
        Assert.Contains("[loc]", result.Value);

        // Cleanup
        _sessionManager.RemoveSession("e5f6a7b8");
    }

    [Fact]
    public void ExplicitSession_ById_Works_WithMultipleSessions()
    {
        _store.Update(CreateBreakModeState());
        _sessionManager.GetOrCreateSession("e5f6a7b8", "OtherApp", @"C:\src\OtherApp.sln");

        // Query by ID
        var result = _service.GetDebugState(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("[loc]", result.Value);

        // Cleanup
        _sessionManager.RemoveSession("e5f6a7b8");
    }

    [Fact]
    public void ExplicitSession_ReturnsSessionNotFound_WhenNotFound()
    {
        var result = _service.GetDebugState(session: "NonExistent");

        Assert.True(result.IsFailure);
        Assert.Equal("Session.NotFound", result.Error.Code);
        Assert.Contains("NonExistent", result.Error.Description);
    }

    [Fact]
    public void DuplicateNames_ResolvedById()
    {
        // Two sessions with the same friendly name but different IDs (different solution paths)
        _store.Update(CreateBreakModeState());
        _sessionManager.GetOrCreateSession("e5f6a7b8", TestSessionName, @"C:\other\TestApp.sln");

        // Query by name should fail because it's ambiguous
        var ambiguous = _service.GetDebugState(session: TestSessionName);
        Assert.True(ambiguous.IsFailure);
        Assert.Equal("Session.Ambiguous", ambiguous.Error.Code);
        Assert.Contains(TestSessionId, ambiguous.Error.Description);
        Assert.Contains("e5f6a7b8", ambiguous.Error.Description);

        // Query by ID should work
        var result = _service.GetDebugState(session: TestSessionId);
        Assert.True(result.IsSuccess);
        Assert.Contains("[loc]", result.Value);

        // Cleanup
        _sessionManager.RemoveSession("e5f6a7b8");
    }

    #endregion

    #region GetDebugState

    [Fact]
    public void GetDebugState_ReturnsNoDebugState_WhenNoStateAvailable()
    {
        var result = _service.GetDebugState(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoState", result.Error.Code);
    }

    [Fact]
    public void GetDebugState_ReturnsNotInBreakMode_WhenNotInBreakMode()
    {
        _store.Update(new DebugState { IsInBreakMode = false });

        var result = _service.GetDebugState(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NotInBreakMode", result.Error.Code);
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

        var result = _service.GetDebugState(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("[loc]", result.Value);
        Assert.Contains("@ Run (App.cs:10) [TestProject]", result.Value);
        Assert.Contains("[locals]", result.Value);
        Assert.Contains("count:int=42", result.Value);
        Assert.Contains("[stack]", result.Value);
        Assert.Contains("0: Run (App.cs:10)", result.Value);
    }

    [Fact]
    public void GetDebugState_OmitsLocationSection_WhenCurrentLocationIsNull()
    {
        var state = CreateBreakModeState(location: null);
        state.CurrentLocation = null;
        _store.Update(state);

        var result = _service.GetDebugState(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("[loc]", result.Value);
    }

    #endregion

    #region GetLocals

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

        var result = _service.GetLocals(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("[locals]", result.Value);
        Assert.Contains("person:Person={Person}", result.Value);
        Assert.Contains(".Name:string=\"Alice\"", result.Value);
        Assert.Contains(".Age:int=30", result.Value);
    }

    [Fact]
    public void GetLocals_ShowsEmptyMessage_WhenNoLocals()
    {
        _store.Update(CreateBreakModeState(locals: new List<LocalVariable>()));

        var result = _service.GetLocals(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal("No local variables in the current scope.", result.Value);
    }

    [Fact]
    public void GetLocals_ReturnsNoDebugState_WhenNoState()
    {
        var result = _service.GetLocals(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoState", result.Error.Code);
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

        var result = _service.GetLocals(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("[!]", result.Value);
    }

    #endregion

    #region GetCallStack

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

        var result = _service.GetCallStack(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("[stack]", result.Value);
        Assert.Contains("0: Inner (Foo.cs:15)", result.Value);
        Assert.Contains("1: Outer (Bar.cs:30)", result.Value);
        Assert.Contains("2: ExternalCall [ext]", result.Value);
    }

    [Fact]
    public void GetCallStack_ReturnsEmptyMessage_WhenNoFrames()
    {
        _store.Update(CreateBreakModeState(callStack: new List<StackFrameInfo>()));

        var result = _service.GetCallStack(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Call stack is empty.", result.Value);
    }

    [Fact]
    public void GetCallStack_ReturnsNotInBreakMode_WhenNotInBreakMode()
    {
        _store.Update(new DebugState { IsInBreakMode = false });

        var result = _service.GetCallStack(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NotInBreakMode", result.Error.Code);
    }

    #endregion

    #region GetSourceContext

    [Fact]
    public void GetSourceContext_ReturnsNoSourceLocation_WhenNoLocation()
    {
        var state = CreateBreakModeState();
        state.CurrentLocation = null;
        _store.Update(state);

        var result = _service.GetSourceContext(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoSourceLocation", result.Error.Code);
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

        var result = _service.GetSourceContext(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("Source file not accessible", result.Value);
        Assert.Contains(@"C:\nonexistent\path\File.cs", result.Value);
    }

    [Fact]
    public void GetSourceContext_ReturnsFormattedSource_WithHighlightedCurrentLine()
    {
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

            var result = _service.GetSourceContext(session: TestSessionId);

            Assert.True(result.IsSuccess);
            Assert.Contains("## Source:", result.Value);
            Assert.Contains("**Function**: `TestMethod`", result.Value);
            Assert.Contains("**Line 15**", result.Value);
            Assert.Contains("```csharp", result.Value);
            Assert.Contains(">>>", result.Value);
            Assert.Contains("// Line 15", result.Value);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region GetBreakpoints

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

        var result = _service.GetBreakpoints(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("[breakpoints]", result.Value);
        Assert.Contains("Controller.cs:50 (on)", result.Value);
        Assert.Contains("HandleRequest", result.Value);
        Assert.Contains("when id > 0", result.Value);
        Assert.Contains("Service.cs:100 (off)", result.Value);
    }

    [Fact]
    public void GetBreakpoints_ReturnsEmptyMessage_WhenNoBreakpoints()
    {
        _store.Update(CreateBreakModeState(breakpoints: new List<BreakpointInfo>()));

        var result = _service.GetBreakpoints(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal("No breakpoints are set.", result.Value);
    }

    [Fact]
    public void GetBreakpoints_ReturnsNoDebugState_WhenNoState()
    {
        var result = _service.GetBreakpoints(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoState", result.Error.Code);
    }

    [Fact]
    public void GetBreakpoints_DoesNotRequireBreakMode()
    {
        var state = new DebugState
        {
            IsInBreakMode = false,
            Breakpoints = new List<BreakpointInfo>
            {
                new() { FilePath = @"C:\src\Test.cs", Line = 1, Enabled = true }
            }
        };
        _store.Update(state);

        var result = _service.GetBreakpoints(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("Test.cs:1 (on)", result.Value);
    }

    #endregion

    #region GetExpressionResult

    [Fact]
    public void GetExpressionResult_ReturnsNoExpressionResult_WhenNoExpressionAvailable()
    {
        var result = _service.GetExpressionResult(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoExpressionResult", result.Error.Code);
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

        var result = _service.GetExpressionResult(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("expr list.Count:int=3", result.Value);
        Assert.DoesNotContain("[!]", result.Value);
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

        var result = _service.GetExpressionResult(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("expr myObj:MyClass={MyClass}", result.Value);
        Assert.Contains(".Id:int=7", result.Value);
        Assert.Contains(".Label:string=\"test\"", result.Value);
    }

    #endregion

    #region ExplainCurrentState

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
        state.CurrentLocation = new SourceLocation
        {
            FilePath = @"C:\nonexistent\Calc.cs",
            Line = 5,
            FunctionName = "Calculate",
            ProjectName = "App"
        };
        _store.Update(state);

        var result = _service.ExplainCurrentState(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("Source file not accessible", result.Value);
        Assert.Contains("x:int=10", result.Value);
        Assert.Contains("0: Calculate (Calc.cs:5)", result.Value);
    }

    [Fact]
    public void ExplainCurrentState_ReturnsNoDebugState_WhenAllSectionsEmpty()
    {
        var result = _service.ExplainCurrentState(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoState", result.Error.Code);
    }

    #endregion

    #region GetBreakpointHistory

    [Fact]
    public void GetBreakpointHistory_ReturnsNoHistory_WhenNoHistory()
    {
        var result = _service.GetBreakpointHistory(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoHistory", result.Error.Code);
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

        var result = _service.GetBreakpointHistory(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("2 snapshots", result.Value);
        Assert.Contains("#0", result.Value);
        Assert.Contains("#1", result.Value);
        Assert.Contains("MethodA", result.Value);
        Assert.Contains("MethodB", result.Value);
        Assert.Contains("A.cs:10", result.Value);
        Assert.Contains("B.cs:20", result.Value);
        Assert.Contains("1 locals", result.Value);
        Assert.Contains("2 locals", result.Value);
    }

    #endregion

    #region GetSnapshot

    [Fact]
    public void GetSnapshot_ReturnsSnapshotNotFound_WhenNotFound()
    {
        var result = _service.GetSnapshot(999, session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.SnapshotNotFound", result.Error.Code);
        Assert.Contains("999", result.Error.Description);
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

        var result = _service.GetSnapshot(0, session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("#0", result.Value);
        Assert.Contains("@ DoWork (File.cs:42) [App]", result.Value);
        Assert.Contains("[locals]", result.Value);
        Assert.Contains("count:int=5", result.Value);
        Assert.Contains("[stack]", result.Value);
        Assert.Contains("0: DoWork (File.cs:42)", result.Value);
    }

    #endregion

    #region ExplainExecutionFlow

    [Fact]
    public void ExplainExecutionFlow_ReturnsNoHistory_WhenNoHistory()
    {
        var result = _service.ExplainExecutionFlow(session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.NoHistory", result.Error.Code);
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

        var result = _service.ExplainExecutionFlow(session: TestSessionId);

        Assert.True(result.IsSuccess);
        // Default detail=changes: first snapshot full, second shows diff
        Assert.Contains("Trace (2 snapshots)", result.Value);
        Assert.Contains("#0", result.Value);
        Assert.Contains("#1", result.Value);
        Assert.Contains("Begin (Start.cs:5)", result.Value);
        Assert.Contains("Process (Middle.cs:15)", result.Value);
        // First snapshot: full locals
        Assert.Contains("input:string=\"hello\"", result.Value);
        // Second snapshot: diff
        Assert.Contains("[changed]", result.Value);
        Assert.Contains("input:string=\"HELLO\" (was \"hello\")", result.Value);
        Assert.Contains("[new]", result.Value);
        Assert.Contains("processed:bool=true", result.Value);
    }

    #endregion

    #region Rolling History Edge Cases (50-snapshot cap)

    /// <summary>
    /// Helper: push N distinct break-mode snapshots with unique data per snapshot.
    /// Each gets a unique file, line, function, locals with members, and a 2-frame call stack.
    /// </summary>
    private void PushSnapshots(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _store.Update(CreateBreakModeState(
                location: new SourceLocation
                {
                    FilePath = $@"C:\src\File{i}.cs",
                    Line = i + 1,
                    Column = 1,
                    FunctionName = $"Method{i}",
                    ProjectName = $"Proj{i}"
                },
                locals: new List<LocalVariable>
                {
                    new()
                    {
                        Name = $"var{i}",
                        Type = "int",
                        Value = $"{i * 10}",
                        IsValidValue = true,
                        Members = new List<LocalVariable>
                        {
                            new() { Name = "Inner", Type = "string", Value = $"\"val{i}\"", IsValidValue = true }
                        }
                    }
                },
                callStack: new List<StackFrameInfo>
                {
                    new() { Index = 0, FunctionName = $"Method{i}", Module = "App.dll", Language = "C#", FilePath = $@"C:\src\File{i}.cs", Line = i + 1 },
                    new() { Index = 1, FunctionName = "Main", Module = "App.dll", Language = "C#", FilePath = @"C:\src\Program.cs", Line = 10 }
                },
                breakpoints: new List<BreakpointInfo>
                {
                    new() { FilePath = $@"C:\src\File{i}.cs", Line = i + 1, Enabled = true, FunctionName = $"Method{i}" }
                }));
        }
    }

    [Fact]
    public void GetSnapshot_ReturnsEvictedMessage_ForOldIndex()
    {
        PushSnapshots(55); // cap=50, so indices 0..4 evicted

        var result = _service.GetSnapshot(0, session: TestSessionId);

        Assert.True(result.IsFailure);
        Assert.Equal("Debugger.SnapshotEvicted", result.Error.Code);
        Assert.Contains("evicted", result.Error.Description);
        Assert.Contains("last 50", result.Error.Description);
        Assert.Contains("#5", result.Error.Description); // oldest available
    }

    [Fact]
    public void GetSnapshot_StillWorks_ForSurvivingIndex()
    {
        PushSnapshots(55); // oldest surviving = index 5

        var result = _service.GetSnapshot(5, session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("#5", result.Value);
        Assert.Contains("Method5", result.Value);
        Assert.Contains("File5.cs:6", result.Value);
        Assert.Contains("var5:int=50", result.Value);
        Assert.Contains("[stack]", result.Value);
    }

    [Fact]
    public void GetBreakpointHistory_ShowsEvictionInfo_WhenRolling()
    {
        PushSnapshots(55);

        var result = _service.GetBreakpointHistory(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("50 of 55", result.Value);
        Assert.Contains("#5", result.Value); // first entry
    }

    [Fact]
    public void GetBreakpointHistory_ShowsSimpleHeader_WhenNoEviction()
    {
        PushSnapshots(10);

        var result = _service.GetBreakpointHistory(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("10 snapshots", result.Value);
        Assert.DoesNotContain(" of ", result.Value);
    }

    [Fact]
    public void ExplainExecutionFlow_AfterEviction_FirstSnapshotGetsFull()
    {
        PushSnapshots(55); // oldest surviving = #5

        var result = _service.ExplainExecutionFlow(session: TestSessionId, detail: "changes");

        Assert.True(result.IsSuccess);
        // First surviving snapshot (#5) should show full locals, not a diff
        Assert.Contains("var5:int=50", result.Value);
        Assert.Contains("[locals]", result.Value);
    }

    [Fact]
    public void ExplainExecutionFlow_AfterEviction_HeaderShowsTotalCaptured()
    {
        PushSnapshots(55);

        var result = _service.ExplainExecutionFlow(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("50 of 55", result.Value);
    }

    [Fact]
    public void ExplainExecutionFlow_Pagination_WithEviction()
    {
        PushSnapshots(55);

        var result = _service.ExplainExecutionFlow(session: TestSessionId, start: 10, count: 3);

        Assert.True(result.IsSuccess);
        Assert.Contains("showing 3 from #10", result.Value);
        Assert.Contains("#10", result.Value);
        Assert.Contains("#11", result.Value);
        Assert.Contains("#12", result.Value);
        Assert.DoesNotContain("#13", result.Value);
    }

    [Fact]
    public void GetSnapshot_AllDataIntact_AfterEviction()
    {
        PushSnapshots(55);

        // Oldest surviving = index 5
        var result = _service.GetSnapshot(5, session: TestSessionId, depth: 2);

        Assert.True(result.IsSuccess);
        // Location
        Assert.Contains("Method5", result.Value);
        Assert.Contains("File5.cs:6", result.Value);
        Assert.Contains("[Proj5]", result.Value);
        // Locals with members
        Assert.Contains("var5:int=50", result.Value);
        Assert.Contains(".Inner:string=\"val5\"", result.Value);
        // Call stack
        Assert.Contains("[stack]", result.Value);
        Assert.Contains("0: Method5 (File5.cs:6)", result.Value);
        Assert.Contains("1: Main (Program.cs:10)", result.Value);
    }

    #endregion

    #region Session isolation

    [Fact]
    public void Sessions_AreIsolated_DifferentStores()
    {
        var otherStore = _sessionManager.GetOrCreateSession("e5f6a7b8", "OtherApp", @"C:\src\OtherApp.sln");

        _store.Update(CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new() { Name = "fromTestApp", Type = "string", Value = "\"test\"", IsValidValue = true }
            }));

        otherStore.Update(CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new() { Name = "fromOtherApp", Type = "string", Value = "\"other\"", IsValidValue = true }
            }));

        var testResult = _service.GetDebugState(session: TestSessionId);
        var otherResult = _service.GetDebugState(session: "OtherApp");

        Assert.True(testResult.IsSuccess);
        Assert.True(otherResult.IsSuccess);
        Assert.Contains("fromTestApp", testResult.Value);
        Assert.DoesNotContain("fromOtherApp", testResult.Value);

        Assert.Contains("fromOtherApp", otherResult.Value);
        Assert.DoesNotContain("fromTestApp", otherResult.Value);

        // Cleanup
        _sessionManager.RemoveSession("e5f6a7b8");
    }

    [Fact]
    public void RemoveSession_MakesItUnavailable()
    {
        _sessionManager.GetOrCreateSession("temp1234", "TempSession", @"C:\src\Temp.sln");
        _sessionManager.RemoveSession("temp1234");

        var result = _service.GetDebugState(session: "temp1234");
        Assert.True(result.IsFailure);
        Assert.Equal("Session.NotFound", result.Error.Code);
    }

    #endregion

    #region ExplainCurrentState (continued)

    [Fact]
    public void ExplainCurrentState_ReturnsPartialContent_WhenSomeSectionsFail()
    {
        var state = CreateBreakModeState(
            locals: new List<LocalVariable>
            {
                new() { Name = "flag", Type = "bool", Value = "true", IsValidValue = true }
            },
            callStack: new List<StackFrameInfo>());
        state.CurrentLocation = null;
        _store.Update(state);

        var result = _service.ExplainCurrentState(session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.Contains("flag:bool=true", result.Value);
        Assert.Contains("Call stack is empty.", result.Value);
    }

    #endregion
}
