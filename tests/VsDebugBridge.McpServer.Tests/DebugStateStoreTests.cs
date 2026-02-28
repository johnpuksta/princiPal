using VsDebugBridge.Contracts;
using VsDebugBridge.McpServer.Services;

namespace VsDebugBridge.McpServer.Tests;

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
