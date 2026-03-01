namespace PrinciPal.Server.Configuration;

public sealed class IdleShutdownOptions
{
    public int PollIntervalSeconds { get; set; } = 10;
    public int GracePeriodSeconds { get; set; } = 30;
    public int InitialConnectionTimeoutSeconds { get; set; } = 300;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
    public TimeSpan GracePeriod => TimeSpan.FromSeconds(GracePeriodSeconds);
    public TimeSpan InitialConnectionTimeout => TimeSpan.FromSeconds(InitialConnectionTimeoutSeconds);
}
