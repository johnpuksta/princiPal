using PrinciPal.Contracts;

namespace PrinciPal.McpServer.Services;

/// <summary>
/// Thread-safe in-memory store for the latest debug state pushed from the
/// Visual Studio extension. The VSIX POSTs state here; MCP tools read it.
/// Also maintains a capped history of breakpoint snapshots for multi-breakpoint analysis.
/// </summary>
public class DebugStateStore
{
    private readonly object _lock = new();
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
    public int TotalCaptured { get { lock (_lock) { return _nextIndex; } } }

    public void Update(DebugState state)
    {
        lock (_lock)
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
    }

    public void UpdateExpression(ExpressionResult result)
    {
        lock (_lock)
        {
            _lastExpression = result;
        }
    }

    public DebugState? GetCurrentState()
    {
        lock (_lock)
        {
            return _currentState;
        }
    }

    public ExpressionResult? GetLastExpression()
    {
        lock (_lock)
        {
            return _lastExpression;
        }
    }

    /// <summary>
    /// Returns a copy of all snapshots in the history, oldest first.
    /// </summary>
    public List<DebugStateSnapshot> GetHistory()
    {
        lock (_lock)
        {
            return new List<DebugStateSnapshot>(_history);
        }
    }

    /// <summary>
    /// Returns a snapshot by its auto-incrementing index, or null if not found.
    /// </summary>
    public DebugStateSnapshot? GetSnapshot(int index)
    {
        lock (_lock)
        {
            return _history.Find(s => s.Index == index);
        }
    }

    /// <summary>
    /// Clears state, expression, and all history.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _currentState = null;
            _lastExpression = null;
            _history.Clear();
            _nextIndex = 0;
        }
    }

    /// <summary>
    /// Clears only the history, keeping current state and expression intact.
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
            _nextIndex = 0;
        }
    }
}
