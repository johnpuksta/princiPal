using PrinciPal.Application.Abstractions;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.Infrastructure.Services;

namespace PrinciPal.Tests.Smoke.Services;

/// <summary>
/// Regression guard tests that assert maximum character counts for realistic scenarios.
/// If a test fails, the failure message includes the actual length for easy threshold tuning.
/// </summary>
public class TokenBudgetTests
{
    private readonly SessionManager _sessionManager = new();
    private readonly IDebugStateStore _store;
    private readonly DebugQueryService _service;

    private const string TestSessionId = "b1c2d3e4";

    public TokenBudgetTests()
    {
        _store = _sessionManager.GetOrCreateSession(TestSessionId, "BudgetApp", @"C:\src\BudgetApp.sln");
        _service = new DebugQueryService(_sessionManager, new SourceFileReader());
    }

    [Fact]
    public void SingleSnapshot_FiveLocals_ThreeFrames()
    {
        _store.Update(CreateState(
            "Calc.cs", 10, "Calculate",
            CreateLocals(5),
            CreateFrames(3)));

        var result = _service.GetSnapshot(0, session: TestSessionId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 400,
            $"SingleSnapshot_FiveLocals_ThreeFrames: {result.Value.Length} chars exceeds 400 budget");
    }

    [Fact]
    public void TenSnapshots_ChangesMode_OneVarChanges()
    {
        for (int i = 0; i < 10; i++)
        {
            var locals = new List<LocalVariable>
            {
                new() { Name = "counter", Type = "int", Value = i.ToString(), IsValidValue = true },
                new() { Name = "name", Type = "string", Value = "\"Alice\"", IsValidValue = true },
                new() { Name = "active", Type = "bool", Value = "true", IsValidValue = true }
            };
            _store.Update(CreateState($"Loop.cs", 10 + i, "RunLoop", locals, CreateFrames(2)));
        }

        var result = _service.ExplainExecutionFlow(session: TestSessionId, detail: "changes", depth: 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 1800,
            $"TenSnapshots_ChangesMode_OneVarChanges: {result.Value.Length} chars exceeds 1800 budget");
    }

    [Fact]
    public void TwentySixSnapshots_DeepLocals_ChangesMode()
    {
        // Realistic: 6 static deep locals + 2 that change each snapshot
        for (int i = 0; i < 26; i++)
        {
            var locals = CreateDeepLocals(6, 3, seed: 0); // static across all snapshots
            locals.Add(new LocalVariable
            {
                Name = "counter", Type = "int", Value = i.ToString(), IsValidValue = true
            });
            locals.Add(new LocalVariable
            {
                Name = "status", Type = "string", Value = $"\"{(i % 2 == 0 ? "even" : "odd")}\"", IsValidValue = true
            });
            _store.Update(CreateState($"Step{i}.cs", 10 + i, $"Step{i}", locals, CreateFrames(8)));
        }

        var result = _service.ExplainExecutionFlow(session: TestSessionId, detail: "changes", depth: 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 8000,
            $"TwentySixSnapshots_DeepLocals_ChangesMode: {result.Value.Length} chars exceeds 8000 budget");
    }

    [Fact]
    public void TenSnapshots_FullMode()
    {
        for (int i = 0; i < 10; i++)
        {
            _store.Update(CreateState($"Step{i}.cs", 10 + i, $"Step{i}",
                CreateLocals(3), CreateFrames(2)));
        }

        var result = _service.ExplainExecutionFlow(session: TestSessionId, detail: "full", depth: 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 3000,
            $"TenSnapshots_FullMode: {result.Value.Length} chars exceeds 3000 budget");
    }

    [Fact]
    public void TwentySixSnapshots_SummaryMode()
    {
        for (int i = 0; i < 26; i++)
        {
            var locals = new List<LocalVariable>
            {
                new() { Name = "counter", Type = "int", Value = i.ToString(), IsValidValue = true },
                new() { Name = "status", Type = "string", Value = $"\"{(i % 2 == 0 ? "even" : "odd")}\"", IsValidValue = true }
            };
            _store.Update(CreateState($"Step{i}.cs", i + 1, $"Step{i}", locals, CreateFrames(3)));
        }

        var result = _service.ExplainExecutionFlow(session: TestSessionId, detail: "summary", depth: 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 2500,
            $"TwentySixSnapshots_SummaryMode: {result.Value.Length} chars exceeds 2500 budget");
    }

    [Fact]
    public void GetLocals_DepthZero_NoExpansion()
    {
        var locals = CreateDeepLocals(8, 5, 0);
        _store.Update(CreateState("Deep.cs", 1, "DeepMethod", locals, new List<StackFrameInfo>()));

        var result = _service.GetLocals(session: TestSessionId, depth: 0);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 350,
            $"GetLocals_DepthZero_NoExpansion: {result.Value.Length} chars exceeds 350 budget");
    }

    [Fact]
    public void GetLocals_DepthTwo_TwoLevels()
    {
        var locals = CreateDeepLocals(8, 3, 0);
        _store.Update(CreateState("Deep.cs", 1, "DeepMethod", locals, new List<StackFrameInfo>()));

        var result = _service.GetLocals(session: TestSessionId, depth: 2);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 1200,
            $"GetLocals_DepthTwo_TwoLevels: {result.Value.Length} chars exceeds 1200 budget");
    }

    [Fact]
    public void Pagination_ThreeOfTwenty()
    {
        for (int i = 0; i < 20; i++)
        {
            _store.Update(CreateState($"Page{i}.cs", i + 1, $"Page{i}",
                CreateLocals(3), CreateFrames(2)));
        }

        var result = _service.ExplainExecutionFlow(session: TestSessionId, detail: "full", depth: 1, start: 10, count: 3);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length <= 1000,
            $"Pagination_ThreeOfTwenty: {result.Value.Length} chars exceeds 1000 budget");
        Assert.Contains("20 total, showing 3 from #10", result.Value);
    }

    #region Helpers

    private static DebugState CreateState(string file, int line, string func,
        List<LocalVariable> locals, List<StackFrameInfo> stack)
    {
        return new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation
            {
                FilePath = $@"C:\src\{file}",
                Line = line,
                FunctionName = func,
                ProjectName = "App"
            },
            Locals = locals,
            CallStack = stack
        };
    }

    private static List<LocalVariable> CreateLocals(int count)
    {
        var locals = new List<LocalVariable>();
        for (int i = 0; i < count; i++)
        {
            locals.Add(new LocalVariable
            {
                Name = $"var{i}",
                Type = "int",
                Value = i.ToString(),
                IsValidValue = true
            });
        }
        return locals;
    }

    private static List<StackFrameInfo> CreateFrames(int count)
    {
        var frames = new List<StackFrameInfo>();
        for (int i = 0; i < count; i++)
        {
            frames.Add(new StackFrameInfo
            {
                Index = i,
                FunctionName = $"Frame{i}",
                Module = "App.dll",
                Language = "C#",
                FilePath = $@"C:\src\Frame{i}.cs",
                Line = 10 + i
            });
        }
        return frames;
    }

    private static List<LocalVariable> CreateDeepLocals(int count, int membersPerVar, int seed)
    {
        var locals = new List<LocalVariable>();
        for (int i = 0; i < count; i++)
        {
            var members = new List<LocalVariable>();
            for (int j = 0; j < membersPerVar; j++)
            {
                members.Add(new LocalVariable
                {
                    Name = $"M{j}",
                    Type = "int",
                    Value = (seed * 100 + i * 10 + j).ToString(),
                    IsValidValue = true
                });
            }
            locals.Add(new LocalVariable
            {
                Name = $"obj{i}",
                Type = "Obj",
                Value = "{Obj}",
                IsValidValue = true,
                Members = members
            });
        }
        return locals;
    }

    #endregion
}
