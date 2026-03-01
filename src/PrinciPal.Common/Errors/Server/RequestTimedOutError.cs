using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors.Server;

public sealed class RequestTimedOutError : ErrorBase
{
    public RequestTimedOutError(string action)
        : base("Server.Timeout",
               $"{action} request timed out.") { }
}
