using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PrinciPal.Application.Abstractions;
using PrinciPal.Server.Configuration;
using PrinciPal.Server.Jobs;
using Quartz;

namespace PrinciPal.Server.Tests.Jobs;

public class IdleShutdownJobTests
{
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();
    private readonly IServerLifecycleManager _lifecycleManager = Substitute.For<IServerLifecycleManager>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IdleShutdownState _state;
    private readonly IdleShutdownOptions _options;
    private readonly IdleShutdownJob _job;
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();

    public IdleShutdownJobTests()
    {
        _options = new IdleShutdownOptions
        {
            PollIntervalSeconds = 10,
            GracePeriodSeconds = 30,
            InitialConnectionTimeoutSeconds = 300
        };

        _state = new IdleShutdownState(_timeProvider);
        _job = new IdleShutdownJob(
            _sessionManager,
            _lifecycleManager,
            _state,
            Options.Create(_options),
            _timeProvider,
            NullLogger<IdleShutdownJob>.Instance);
    }

    [Fact]
    public async Task WaitingForFirstSession_NoSessions_NoTimeout_DoesNothing()
    {
        _sessionManager.SessionCount.Returns(0);
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        await _job.Execute(_context);

        Assert.Equal(WatchdogPhase.WaitingForFirstSession, _state.Phase);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task WaitingForFirstSession_SessionConnects_TransitionsToActive()
    {
        _sessionManager.SessionCount.Returns(1);

        await _job.Execute(_context);

        Assert.Equal(WatchdogPhase.Active, _state.Phase);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task WaitingForFirstSession_TimeoutExpired_ShutsDown()
    {
        _sessionManager.SessionCount.Returns(0);
        _timeProvider.Advance(TimeSpan.FromMinutes(6));

        await _job.Execute(_context);

        _lifecycleManager.Received(1).RequestShutdown(Arg.Is<string>(s => s.Contains("initial timeout")));
    }

    [Fact]
    public async Task Active_SessionsExist_StaysActive()
    {
        _state.Phase = WatchdogPhase.Active;
        _sessionManager.SessionCount.Returns(2);

        await _job.Execute(_context);

        Assert.Equal(WatchdogPhase.Active, _state.Phase);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task Active_NoSessions_TransitionsToGracePeriod()
    {
        _state.Phase = WatchdogPhase.Active;
        _sessionManager.SessionCount.Returns(0);

        await _job.Execute(_context);

        Assert.Equal(WatchdogPhase.GracePeriod, _state.Phase);
        Assert.NotNull(_state.GracePeriodStartedAt);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task GracePeriod_SessionReconnects_TransitionsBackToActive()
    {
        _state.Phase = WatchdogPhase.GracePeriod;
        _state.GracePeriodStartedAt = _timeProvider.GetUtcNow();
        _sessionManager.SessionCount.Returns(1);

        await _job.Execute(_context);

        Assert.Equal(WatchdogPhase.Active, _state.Phase);
        Assert.Null(_state.GracePeriodStartedAt);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task GracePeriod_NotYetExpired_DoesNotShutDown()
    {
        _state.Phase = WatchdogPhase.GracePeriod;
        _state.GracePeriodStartedAt = _timeProvider.GetUtcNow();
        _sessionManager.SessionCount.Returns(0);
        _timeProvider.Advance(TimeSpan.FromSeconds(15));

        await _job.Execute(_context);

        Assert.Equal(WatchdogPhase.GracePeriod, _state.Phase);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task GracePeriod_Expired_ShutsDown()
    {
        _state.Phase = WatchdogPhase.GracePeriod;
        _state.GracePeriodStartedAt = _timeProvider.GetUtcNow();
        _sessionManager.SessionCount.Returns(0);
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        await _job.Execute(_context);

        _lifecycleManager.Received(1).RequestShutdown(Arg.Is<string>(s => s.Contains("Grace period")));
    }

    [Fact]
    public async Task FullLifecycle_WaitConnect_Disconnect_Grace_Shutdown()
    {
        // Phase 1: Waiting, no sessions
        _sessionManager.SessionCount.Returns(0);
        await _job.Execute(_context);
        Assert.Equal(WatchdogPhase.WaitingForFirstSession, _state.Phase);

        // Phase 2: Session connects
        _sessionManager.SessionCount.Returns(1);
        await _job.Execute(_context);
        Assert.Equal(WatchdogPhase.Active, _state.Phase);

        // Phase 3: Session disconnects → grace period
        _sessionManager.SessionCount.Returns(0);
        await _job.Execute(_context);
        Assert.Equal(WatchdogPhase.GracePeriod, _state.Phase);

        // Phase 4: Grace period not yet expired
        _timeProvider.Advance(TimeSpan.FromSeconds(15));
        await _job.Execute(_context);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());

        // Phase 5: Grace period expired → shutdown
        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        await _job.Execute(_context);
        _lifecycleManager.Received(1).RequestShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task GracePeriod_Resets_WhenSessionReconnects()
    {
        // Enter active, then grace period
        _state.Phase = WatchdogPhase.Active;
        _sessionManager.SessionCount.Returns(0);
        await _job.Execute(_context);
        Assert.Equal(WatchdogPhase.GracePeriod, _state.Phase);
        var firstGraceStart = _state.GracePeriodStartedAt;

        // Advance 20s (within grace), reconnect
        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        _sessionManager.SessionCount.Returns(1);
        await _job.Execute(_context);
        Assert.Equal(WatchdogPhase.Active, _state.Phase);
        Assert.Null(_state.GracePeriodStartedAt);

        // Disconnect again — new grace period starts
        _sessionManager.SessionCount.Returns(0);
        await _job.Execute(_context);
        Assert.Equal(WatchdogPhase.GracePeriod, _state.Phase);
        Assert.NotEqual(firstGraceStart, _state.GracePeriodStartedAt);

        // Advance only 15s — should NOT shut down (new grace clock)
        _timeProvider.Advance(TimeSpan.FromSeconds(15));
        await _job.Execute(_context);
        _lifecycleManager.DidNotReceive().RequestShutdown(Arg.Any<string>());

        // Advance another 20s — total 35s from new grace start → shutdown
        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        await _job.Execute(_context);
        _lifecycleManager.Received(1).RequestShutdown(Arg.Any<string>());
    }
}
