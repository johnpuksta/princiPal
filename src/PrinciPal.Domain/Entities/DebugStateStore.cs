using System;
using System.Collections.Generic;
using PrinciPal.Domain.ValueObjects;

namespace PrinciPal.Domain.Entities;

/// <summary>
/// Pure domain model for debug state storage. Handles capped history,
/// eviction, and snapshotting. Not thread-safe — wrap in a thread-safe
/// decorator for concurrent access.
/// </summary>
public class DebugStateStore
{
    private DebugState? _currentState;
    private ExpressionResult? _lastExpression;
    private readonly List<DebugStateSnapshot> _history = new();
    private int _nextIndex;

    /// <summary>
    /// Maximum number of snapshots to keep in history.
    /// Oldest entries are evicted when the cap is reached.
    /// </summary>
    public int MaxHistorySize { get; set; } = 50;

    /// <summary>
    /// Total number of snapshots ever captured (including evicted ones).
    /// Use to distinguish evicted snapshots from never-existed ones.
    /// </summary>
    public int TotalCaptured => _nextIndex;

    public void Update(DebugState state)
    {
        _currentState = state;

        // Only snapshot break-mode states (actual breakpoint hits)
        if (state.IsInBreakMode)
        {
            if (_history.Count >= MaxHistorySize)
            {
                _history.RemoveAt(0);
            }

            _history.Add(new DebugStateSnapshot
            {
                Index = _nextIndex++,
                CapturedAt = DateTime.UtcNow,
                State = state
            });
        }
    }

    public void UpdateExpression(ExpressionResult result)
    {
        _lastExpression = result;
    }

    public DebugState? GetCurrentState()
    {
        return _currentState;
    }

    public ExpressionResult? GetLastExpression()
    {
        return _lastExpression;
    }

    /// <summary>
    /// Returns a copy of all snapshots in the history, oldest first.
    /// </summary>
    public List<DebugStateSnapshot> GetHistory()
    {
        return new List<DebugStateSnapshot>(_history);
    }

    /// <summary>
    /// Returns a snapshot by its auto-incrementing index, or null if not found.
    /// </summary>
    public DebugStateSnapshot? GetSnapshot(int index)
    {
        return _history.Find(s => s.Index == index);
    }

    /// <summary>
    /// Clears state, expression, and all history.
    /// </summary>
    public void Clear()
    {
        _currentState = null;
        _lastExpression = null;
        _history.Clear();
        _nextIndex = 0;
    }

    /// <summary>
    /// Clears only the history, keeping current state and expression intact.
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
        _nextIndex = 0;
    }
}
