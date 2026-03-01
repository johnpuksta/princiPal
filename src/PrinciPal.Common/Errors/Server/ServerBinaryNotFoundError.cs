using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors.Server;

public sealed class ServerBinaryNotFoundError : ErrorBase
{
    public ServerBinaryNotFoundError(string exePath, string devMarkerPath)
        : base("Server.BinaryNotFound",
               $"Server not found. Checked:\n  - {exePath}\n  - {devMarkerPath}") { }
}
