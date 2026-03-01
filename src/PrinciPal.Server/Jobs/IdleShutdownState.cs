namespace PrinciPal.Server.Jobs;

public sealed class IdleShutdownState
{
    private readonly TimeProvider _timeProvider;

    public IdleShutdownState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        StartedAt = _timeProvider.GetUtcNow();
    }

    public WatchdogPhase Phase { get; set; } = WatchdogPhase.WaitingForFirstSession;
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? GracePeriodStartedAt { get; set; }
}
