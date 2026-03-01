using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors.Server;

public sealed class ServerUnreachableError : ErrorBase
{
    public ServerUnreachableError(string url, string detail)
        : base("Server.Unreachable",
               $"MCP server not reachable at {url}. {detail}") { }
}
