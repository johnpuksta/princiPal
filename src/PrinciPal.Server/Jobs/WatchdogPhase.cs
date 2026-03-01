namespace PrinciPal.Server.Jobs;

public enum WatchdogPhase
{
    WaitingForFirstSession,
    Active,
    GracePeriod
}
