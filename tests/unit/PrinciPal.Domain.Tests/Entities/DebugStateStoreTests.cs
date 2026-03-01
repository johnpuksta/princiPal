using PrinciPal.Domain.ValueObjects;
using PrinciPal.Domain.Entities;

namespace PrinciPal.Domain.Tests.Entities;

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
    public void TotalCaptured_TracksLifetimeCount()
    {
        for (int i = 0; i < 5; i++)
            _store.Update(new DebugState { IsInBreakMode = true });

        Assert.Equal(5, _store.TotalCaptured);
    }

    [Fact]
    public void TotalCaptured_KeepsCountingAfterEviction()
    {
        _store.MaxHistorySize = 3;

        for (int i = 0; i < 5; i++)
            _store.Update(new DebugState { IsInBreakMode = true });

        Assert.Equal(5, _store.TotalCaptured);
        Assert.Equal(3, _store.GetHistory().Count);
    }

    [Fact]
    public void GetSnapshot_ReturnsNull_ForEvictedIndex()
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

        Assert.Null(_store.GetSnapshot(0));
        Assert.Null(_store.GetSnapshot(1));
        Assert.NotNull(_store.GetSnapshot(2));
        Assert.Equal("File2.cs", _store.GetSnapshot(2)!.State.CurrentLocation!.FilePath);
    }

    [Fact]
    public void UpdateExpression_OverwritesPreviousExpression()
    {
        _store.UpdateExpression(new ExpressionResult { Expression = "a", Value = "1", Type = "int", IsValid = true });
        _store.UpdateExpression(new ExpressionResult { Expression = "b", Value = "2", Type = "int", IsValid = true });

        var result = _store.GetLastExpression();

        Assert.NotNull(result);
        Assert.Equal("b", result.Expression);
        Assert.Equal("2", result.Value);
    }

    [Fact]
    public void GetHistory_ReturnsCopy_MutationsDoNotAffectInternalState()
    {
        _store.Update(new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 1, FunctionName = "A" }
        });

        var history = _store.GetHistory();
        history.Clear();

        var historyAgain = _store.GetHistory();
        Assert.Single(historyAgain);
    }

    [Fact]
    public void TotalCaptured_IsZero_Initially()
    {
        Assert.Equal(0, _store.TotalCaptured);
    }

    [Fact]
    public void TotalCaptured_DoesNotCountNonBreakModeUpdates()
    {
        _store.Update(new DebugState { IsInBreakMode = false });
        _store.Update(new DebugState { IsInBreakMode = false });
        _store.Update(new DebugState { IsInBreakMode = true });

        Assert.Equal(1, _store.TotalCaptured);
    }

    [Fact]
    public void Clear_ResetsTotalCapturedToZero()
    {
        _store.Update(new DebugState { IsInBreakMode = true });
        _store.Update(new DebugState { IsInBreakMode = true });
        Assert.Equal(2, _store.TotalCaptured);

        _store.Clear();

        Assert.Equal(0, _store.TotalCaptured);
    }

    [Fact]
    public void Update_DoesNotEvict_WhenExactlyAtMaxHistorySize()
    {
        _store.MaxHistorySize = 3;

        for (int i = 0; i < 3; i++)
        {
            _store.Update(new DebugState
            {
                IsInBreakMode = true,
                CurrentLocation = new SourceLocation { FilePath = $"File{i}.cs", Line = i, FunctionName = $"F{i}" }
            });
        }

        var history = _store.GetHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal(0, history[0].Index);
        Assert.Equal(1, history[1].Index);
        Assert.Equal(2, history[2].Index);
    }

    [Fact]
    public void GetHistory_ReturnsOldestFirst_AfterEviction()
    {
        _store.MaxHistorySize = 3;

        for (int i = 0; i < 6; i++)
        {
            _store.Update(new DebugState
            {
                IsInBreakMode = true,
                CurrentLocation = new SourceLocation { FilePath = $"File{i}.cs", Line = i, FunctionName = $"F{i}" }
            });
        }

        var history = _store.GetHistory();
        Assert.Equal(3, history.Count);
        Assert.True(history[0].Index < history[1].Index);
        Assert.True(history[1].Index < history[2].Index);
        Assert.Equal(3, history[0].Index);
        Assert.Equal(5, history[2].Index);
    }

    [Fact]
    public void Snapshot_CapturedAt_IsPopulated()
    {
        var before = DateTime.UtcNow;
        _store.Update(new DebugState { IsInBreakMode = true });
        var after = DateTime.UtcNow;

        var snapshot = _store.GetSnapshot(0);

        Assert.NotNull(snapshot);
        Assert.InRange(snapshot.CapturedAt, before, after);
    }
}
