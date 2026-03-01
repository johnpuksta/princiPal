using PrinciPal.Application.Abstractions;
using PrinciPal.Domain.Entities;
using PrinciPal.Domain.ValueObjects;

namespace PrinciPal.Infrastructure.Services;

/// <summary>
/// Thread-safe wrapper around <see cref="DebugStateStore"/>.
/// All method calls are serialized via a lock to guarantee safe concurrent access.
/// </summary>
public class ThreadSafeDebugStateStore : IDebugStateStore
{
    private readonly object _lock = new();
    private readonly DebugStateStore _inner = new();

    public int MaxHistorySize
    {
        get { lock (_lock) { return _inner.MaxHistorySize; } }
        set { lock (_lock) { _inner.MaxHistorySize = value; } }
    }

    public int TotalCaptured
    {
        get { lock (_lock) { return _inner.TotalCaptured; } }
    }

    public void Update(DebugState state)
    {
        lock (_lock) { _inner.Update(state); }
    }

    public void UpdateExpression(ExpressionResult result)
    {
        lock (_lock) { _inner.UpdateExpression(result); }
    }

    public DebugState? GetCurrentState()
    {
        lock (_lock) { return _inner.GetCurrentState(); }
    }

    public ExpressionResult? GetLastExpression()
    {
        lock (_lock) { return _inner.GetLastExpression(); }
    }

    public List<DebugStateSnapshot> GetHistory()
    {
        lock (_lock) { return _inner.GetHistory(); }
    }

    public DebugStateSnapshot? GetSnapshot(int index)
    {
        lock (_lock) { return _inner.GetSnapshot(index); }
    }

    public void Clear()
    {
        lock (_lock) { _inner.Clear(); }
    }

    public void ClearHistory()
    {
        lock (_lock) { _inner.ClearHistory(); }
    }
}
