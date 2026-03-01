namespace PrinciPal.Server.Endpoints;

internal static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "running" }));
    }
}
