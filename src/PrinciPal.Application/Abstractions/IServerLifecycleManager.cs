namespace PrinciPal.Application.Abstractions;

public interface IServerLifecycleManager
{
    void RequestShutdown(string reason);
}
