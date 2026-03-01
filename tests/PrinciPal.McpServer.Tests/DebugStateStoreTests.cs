using PrinciPal.Contracts;
using PrinciPal.McpServer.Services;

namespace PrinciPal.McpServer.Tests;

public class DebugStateStoreTests
{
    private readonly DebugStateStore _store = new();

    [Fact]
    public void GetCurrentState_ReturnsNull_WhenNoStateHasBeenStored()
    {
        var result = _store.GetCurrentState();

        Assert.Null(result);
    }

    [Fact]
    public void GetLastExpression_ReturnsNull_WhenNoExpressionHasBeenStored()
    {
        var result = _store.GetLastExpression();

        Assert.Null(result);
    }

    [Fact]
    public void Update_StoresState_AndGetCurrentStateRetrievesIt()
    {
        var state = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation
            {
                FilePath = @"C:\project\Program.cs",
                Line = 42,
                FunctionName = "Main",
                ProjectName = "MyApp"
            }
        };

        _store.Update(state);
        var result = _store.GetCurrentState();

        Assert.NotNull(result);
        Assert.True(result.IsInBreakMode);
        Assert.Equal(@"C:\project\Program.cs", result.CurrentLocation!.FilePath);
        Assert.Equal(42, result.CurrentLocation.Line);
    }

    [Fact]
    public void Update_OverwritesPreviousState()
    {
        var first = new DebugState { IsInBreakMode = true };
        var second = new DebugState { IsInBreakMode = false };

        _store.Update(first);
        _store.Update(second);
        var result = _store.GetCurrentState();

        Assert.NotNull(result);
        Assert.False(result.IsInBreakMode);
    }

    [Fact]
    public void UpdateExpression_StoresExpression_AndGetLastExpressionRetrievesIt()
    {
        var expression = new ExpressionResult
        {
            Expression = "myVar.Count",
            Value = "5",
            Type = "int",
            IsValid = true
        };

        _store.UpdateExpression(expression);
        var result = _store.GetLastExpression();

        Assert.NotNull(result);
        Assert.Equal("myVar.Count", result.Expression);
        Assert.Equal("5", result.Value);
        Assert.Equal("int", result.Type);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Clear_ResetsStateToNull()
    {
        _store.Update(new DebugState { IsInBreakMode = true });
        _store.UpdateExpression(new ExpressionResult { Expression = "x", Value = "1", Type = "int", IsValid = true });

        _store.Clear();

        Assert.Null(_store.GetCurrentState());
        Assert.Null(_store.GetLastExpression());
    }

    [Fact]
    public void Clear_IsIdempotent_WhenAlreadyEmpty()
    {
        _store.Clear();

        Assert.Null(_store.GetCurrentState());
        Assert.Null(_store.GetLastExpression());
    }

    // =================================================================
    // History
    // =================================================================

    [Fact]
    public void GetHistory_ReturnsEmpty_WhenNoBreakModeUpdates()
    {
        _store.Update(new DebugState { IsInBreakMode = false });

        Assert.Empty(_store.GetHistory());
    }

    [Fact]
    public void Update_AppendsToHistory_WhenInBreakMode()
    {
        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 1, FunctionName = "A" }
        });
        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "B.cs", Line = 2, FunctionName = "B" }
        });

        var history = _store.GetHistory();

        Assert.Equal(2, history.Count);
        Assert.Equal(0, history[0].Index);
        Assert.Equal(1, history[1].Index);
        Assert.Equal("A.cs", history[0].State.CurrentLocation!.FilePath);
        Assert.Equal("B.cs", history[1].State.CurrentLocation!.FilePath);
    }

    [Fact]
    public void Update_DoesNotAppendToHistory_WhenNotInBreakMode()
    {
        _store.Update(new DebugState { IsInBreakMode = true });
        _store.Update(new DebugState { IsInBreakMode = false });

        Assert.Single(_store.GetHistory());
    }

    [Fact]
    public void Update_EvictsOldest_WhenHistoryExceedsMax()
    {
        _store.MaxHistorySize = 3;

        for (int i = 0; i < 5; i++)
        {
            _store.Update(new DebugState
            {
                IsInBreakMode = true,
                CurrentLocation = new SourceLocation { FilePath = $"File{i}.cs", Line = i, FunctionName = $"F{i}" }
            });
        }

        var history = _store.GetHistory();

        Assert.Equal(3, history.Count);
        // Oldest two (index 0, 1) should be evicted; remaining are indices 2, 3, 4
        Assert.Equal(2, history[0].Index);
        Assert.Equal(3, history[1].Index);
        Assert.Equal(4, history[2].Index);
    }

    [Fact]
    public void GetSnapshot_ReturnsCorrectSnapshot_ByIndex()
    {
        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 10, FunctionName = "First" }
        });
        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "B.cs", Line = 20, FunctionName = "Second" }
        });

        var snapshot = _store.GetSnapshot(1);

        Assert.NotNull(snapshot);
        Assert.Equal("B.cs", snapshot.State.CurrentLocation!.FilePath);
        Assert.Equal("Second", snapshot.State.CurrentLocation.FunctionName);
    }

    [Fact]
    public void GetSnapshot_ReturnsNull_ForNonexistentIndex()
    {
        Assert.Null(_store.GetSnapshot(999));
    }

    [Fact]
    public void ClearHistory_ClearsOnlyHistory_KeepsCurrentState()
    {
        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 1, FunctionName = "A" }
        });
        _store.UpdateExpression(new ExpressionResult { Expression = "x", Value = "1", Type = "int", IsValid = true });

        _store.ClearHistory();

        Assert.Empty(_store.GetHistory());
        Assert.NotNull(_store.GetCurrentState());
        Assert.NotNull(_store.GetLastExpression());
    }

    [Fact]
    public void Clear_AlsoClearsHistory()
    {
        _store.Update(new DebugState { IsInBreakMode = true });
        _store.Update(new DebugState { IsInBreakMode = true });

        _store.Clear();

        Assert.Empty(_store.GetHistory());
        Assert.Null(_store.GetCurrentState());
    }

    [Fact]
    public void ClearHistory_ResetsIndexCounter()
    {
        _store.Update(new DebugState { IsInBreakMode = true });
        _store.Update(new DebugState { IsInBreakMode = true });
        _store.ClearHistory();

        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "New.cs", Line = 1, FunctionName = "New" }
        });

        var history = _store.GetHistory();
        Assert.Single(history);
        Assert.Equal(0, history[0].Index);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        var store = new DebugStateStore();
        var exceptions = new List<Exception>();

        var tasks = new List<Task>();

        // Writers
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    store.Update(new DebugState
                    {
                        IsInBreakMode = index % 2 == 0,
                        CurrentLocation = new SourceLocation
                        {
                            FilePath = $"file{index}.cs",
                            Line = index,
                            FunctionName = $"Method{index}",
                            ProjectName = "TestProject"
                        }
                    });
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        // Expression writers
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    store.UpdateExpression(new ExpressionResult
                    {
                        Expression = $"expr{index}",
                        Value = $"{index}",
                        Type = "int",
                        IsValid = true
                    });
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        // Readers
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    _ = store.GetCurrentState();
                    _ = store.GetLastExpression();
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        // Clearers
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    store.Clear();
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }
}
