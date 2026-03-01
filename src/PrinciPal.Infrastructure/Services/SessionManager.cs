using System.Collections.Concurrent;
using PrinciPal.Application.Abstractions;
using PrinciPal.Common.Errors.Session;
using PrinciPal.Common.Options;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;

namespace PrinciPal.Infrastructure.Services;

/// <summary>
/// Manages multiple VS debug sessions. Each session is keyed by a unique ID
/// (short hash of the solution path) and has a friendly name (solution filename).
/// Each session gets its own <see cref="IDebugStateStore"/> instance.
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Gets or creates a session, returning its <see cref="IDebugStateStore"/>.
    /// Auto-registers the session on first access.
    /// </summary>
    public IDebugStateStore GetOrCreateSession(string sessionId, string? name = null, string? solutionPath = null)
    {
        var entry = _sessions.GetOrAdd(sessionId, id => new SessionEntry
        {
            Store = new ThreadSafeDebugStateStore(),
            Info = new SessionInfo
            {
                SessionId = id,
                Name = name ?? "",
                SolutionPath = solutionPath ?? "",
                ConnectedAt = DateTime.UtcNow
            }
        });

        // Update metadata if provided and was previously empty
        if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(entry.Info.Name))
            entry.Info.Name = name;
        if (!string.IsNullOrEmpty(solutionPath) && string.IsNullOrEmpty(entry.Info.SolutionPath))
            entry.Info.SolutionPath = solutionPath;

        return entry.Store;
    }

    /// <summary>
    /// Gets a session's store by its unique ID, or None if not found.
    /// </summary>
    public Option<IDebugStateStore> GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var entry)
            ? Option.Some(entry.Store)
            : Option<IDebugStateStore>.None;
    }

    /// <summary>
    /// Resolves a query string (name or ID) to a session store.
    /// Returns Success(store) on match, or Failure with appropriate error.
    /// </summary>
    public Result<IDebugStateStore> ResolveByNameOrId(string query)
    {
        // Try exact ID match first
        if (_sessions.TryGetValue(query, out var entry))
            return Result<IDebugStateStore>.Success(entry.Store);

        // Try name match
        var matches = _sessions.Values
            .Where(e => string.Equals(e.Info.Name, query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1)
            return Result<IDebugStateStore>.Success(matches[0].Store);

        if (matches.Count > 1)
        {
            var lines = matches.Select(m => $"  {m.Info.Name} [{m.Info.SessionId}] - {m.Info.SolutionPath}");
            return new AmbiguousSessionError(query, string.Join("\n", lines));
        }

        return new SessionNotFoundError(query);
    }

    /// <summary>
    /// Removes a session and all its state.
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Returns info for all active sessions. HasDebugState is computed dynamically.
    /// </summary>
    public List<SessionInfo> GetAllSessions()
    {
        var result = new List<SessionInfo>();
        foreach (var kvp in _sessions)
        {
            var info = kvp.Value.Info;
            var state = kvp.Value.Store.GetCurrentState();
            result.Add(new SessionInfo
            {
                SessionId = info.SessionId,
                Name = info.Name,
                SolutionPath = info.SolutionPath,
                ConnectedAt = info.ConnectedAt,
                HasDebugState = state is { IsInBreakMode: true }
            });
        }
        return result;
    }

    private class SessionEntry
    {
        public required IDebugStateStore Store { get; init; }
        public required SessionInfo Info { get; init; }
    }
}
