using VsDebugBridge.Contracts;

namespace VsDebugBridge.McpServer.Services;

/// <summary>
/// Thread-safe in-memory store for the latest debug state pushed from the
/// Visual Studio extension. The VSIX POSTs state here; MCP tools read it.
/// </summary>
public class DebugStateStore
{
    private readonly object _lock = new();
    private DebugState? _currentState;
    private ExpressionResult? _lastExpression;

    public void Update(DebugState state)
    {
        lock (_lock)
        {
            _currentState = state;
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

    public void Clear()
    {
        lock (_lock)
        {
            _currentState = null;
            _lastExpression = null;
        }
    }
}
