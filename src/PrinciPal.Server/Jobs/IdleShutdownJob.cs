using Microsoft.Extensions.Options;
using PrinciPal.Application.Abstractions;
using PrinciPal.Server.Configuration;
using Quartz;

namespace PrinciPal.Server.Jobs;

[DisallowConcurrentExecution]
public sealed class IdleShutdownJob : IJob
{
    private readonly ISessionManager _sessionManager;
    private readonly IServerLifecycleManager _lifecycleManager;
    private readonly IdleShutdownState _state;
    private readonly IdleShutdownOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdleShutdownJob> _logger;

    public IdleShutdownJob(
        ISessionManager sessionManager,
        IServerLifecycleManager lifecycleManager,
        IdleShutdownState state,
        IOptions<IdleShutdownOptions> options,
        TimeProvider timeProvider,
        ILogger<IdleShutdownJob> logger)
    {
        _sessionManager = sessionManager;
        _lifecycleManager = lifecycleManager;
        _state = state;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        var now = _timeProvider.GetUtcNow();
        var sessionCount = _sessionManager.SessionCount;

        switch (_state.Phase)
        {
            case WatchdogPhase.WaitingForFirstSession:
                if (sessionCount > 0)
                {
                    _logger.LogInformation("First session connected. Transitioning to Active.");
                    _state.Phase = WatchdogPhase.Active;
                }
                else if (now - _state.StartedAt >= _options.InitialConnectionTimeout)
                {
                    _lifecycleManager.RequestShutdown("No session connected within initial timeout.");
                }
                break;

            case WatchdogPhase.Active:
                if (sessionCount == 0)
                {
                    _logger.LogInformation("All sessions disconnected. Entering grace period.");
                    _state.Phase = WatchdogPhase.GracePeriod;
                    _state.GracePeriodStartedAt = now;
                }
                break;

            case WatchdogPhase.GracePeriod:
                if (sessionCount > 0)
                {
                    _logger.LogInformation("Session reconnected during grace period. Returning to Active.");
                    _state.Phase = WatchdogPhase.Active;
                    _state.GracePeriodStartedAt = null;
                }
                else if (now - _state.GracePeriodStartedAt >= _options.GracePeriod)
                {
                    _lifecycleManager.RequestShutdown("Grace period expired with no active sessions.");
                }
                break;
        }

        return Task.CompletedTask;
    }
}
