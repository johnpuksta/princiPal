using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors.Extension;

public sealed class ComReadError : ErrorBase
{
    public ComReadError(string component)
        : base("Extension.ComReadFailed",
               $"Error reading {component} from the debugger.") { }

    public ComReadError(string component, string detail)
        : base("Extension.ComReadFailed",
               $"Error reading {component}: {detail}") { }
}
