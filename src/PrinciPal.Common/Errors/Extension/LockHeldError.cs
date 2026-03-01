using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors.Extension;

public sealed class LockHeldError : ErrorBase
{
    public LockHeldError(int port, int ownerPid)
        : base("Extension.LockHeld",
               $"Another instance (PID {ownerPid}) is starting the server on port {port}.") { }
}
