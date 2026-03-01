using PrinciPal.Application.Abstractions;

namespace PrinciPal.Server.Services;

internal sealed class ServerLifecycleManager : IServerLifecycleManager
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ServerLifecycleManager> _logger;
    private int _shutdownRequested;

    public ServerLifecycleManager(IHostApplicationLifetime lifetime, ILogger<ServerLifecycleManager> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public void RequestShutdown(string reason)
    {
        if (Interlocked.CompareExchange(ref _shutdownRequested, 1, 0) != 0)
        {
            _logger.LogDebug("Shutdown already in progress, ignoring: {Reason}", reason);
            return;
        }

        _logger.LogInformation("Shutdown requested: {Reason}", reason);
        _lifetime.StopApplication();
    }
}
