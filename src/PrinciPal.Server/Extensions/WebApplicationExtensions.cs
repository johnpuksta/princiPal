using PrinciPal.Server.Endpoints;

namespace PrinciPal.Server.Extensions;

internal static class WebApplicationExtensions
{
    public static WebApplication MapPrinciPalEndpoints(this WebApplication app)
    {
        HealthEndpoints.Map(app);
        SessionEndpoints.Map(app);
        DebugStateEndpoints.Map(app);
        app.MapMcp();

        return app;
    }
}
