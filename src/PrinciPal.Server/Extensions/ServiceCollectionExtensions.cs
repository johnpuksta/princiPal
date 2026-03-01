using System.Reflection;
using PrinciPal.Application.Abstractions;
using PrinciPal.Infrastructure.Services;
using PrinciPal.Server.Configuration;
using PrinciPal.Server.Jobs;
using PrinciPal.Server.Services;
using Quartz;

namespace PrinciPal.Server.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrinciPalServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCoreServices();
        services.AddIdleShutdownWatchdog(configuration);
        services.AddMcpServer(configuration);

        return services;
    }

    private static void AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<ISourceFileReader, SourceFileReader>();
        services.AddSingleton<IDebugQueryService, DebugQueryService>();
    }

    private static void AddIdleShutdownWatchdog(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IServerLifecycleManager, ServerLifecycleManager>();
        services.AddSingleton<IdleShutdownState>();
        services.AddSingleton(TimeProvider.System);

        services.Configure<IdleShutdownOptions>(
            configuration.GetSection("IdleShutdown"));

        services.AddQuartz(q =>
        {
            var jobKey = JobKey.Create(nameof(IdleShutdownJob));
            q.AddJob<IdleShutdownJob>(jobKey);
            q.AddTrigger(t => t
                .ForJob(jobKey)
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(10)
                    .RepeatForever()));
        });
        services.AddQuartzHostedService();
    }

    private static void AddMcpServer(this IServiceCollection services, IConfiguration configuration)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "princiPal",
                Version = version,
            };
        })
        .WithHttpTransport()
        .WithToolsFromAssembly();
    }
}
