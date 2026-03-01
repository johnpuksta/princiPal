using PrinciPal.Domain.ValueObjects;
using PrinciPal.Infrastructure.Services;

namespace PrinciPal.Infrastructure.Tests.Services;

public class ThreadSafeDebugStateStoreTests
{
    [Fact]
    public void Update_And_GetCurrentState_RoundTrip()
    {
        var store = new ThreadSafeDebugStateStore();
        var state = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation
            {
                FilePath = "Test.cs",
                Line = 1,
                FunctionName = "Run"
            }
        };

        store.Update(state);
        var result = store.GetCurrentState();

        Assert.NotNull(result);
        Assert.True(result.IsInBreakMode);
        Assert.Equal("Test.cs", result.CurrentLocation!.FilePath);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        var store = new ThreadSafeDebugStateStore();
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
