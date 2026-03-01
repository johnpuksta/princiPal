using PrinciPal.Application.Abstractions;

namespace PrinciPal.Server.Endpoints;

internal static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/sessions");

        group.MapGet("/", (ISessionManager mgr) => Results.Ok(mgr.GetAllSessions()));

        group.MapPost("/{sessionId}", (ISessionManager mgr, string sessionId, string? name, string? path) =>
        {
            mgr.GetOrCreateSession(sessionId, name, path);
            return Results.Ok();
        });

        group.MapDelete("/{sessionId}", (ISessionManager mgr, string sessionId) =>
        {
            mgr.RemoveSession(sessionId);
            return Results.Ok();
        });
    }
}
