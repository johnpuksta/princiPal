using PrinciPal.Common.Options;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;

namespace PrinciPal.Application.Abstractions;

public interface ISessionManager
{
    int SessionCount { get; }
    IDebugStateStore GetOrCreateSession(string sessionId, string? name = null, string? solutionPath = null);
    Option<IDebugStateStore> GetSession(string sessionId);
    Result<IDebugStateStore> ResolveByNameOrId(string query);
    void RemoveSession(string sessionId);
    List<SessionInfo> GetAllSessions();
}
