using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors.Extension;

public sealed class LockFileCorruptError : ErrorBase
{
    public LockFileCorruptError(int port)
        : base("Extension.LockCorrupt",
               $"Lock file for port {port} exists but could not be read or parsed.") { }
}
