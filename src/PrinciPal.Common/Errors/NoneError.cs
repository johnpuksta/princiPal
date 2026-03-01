using PrinciPal.Common.Abstractions;

namespace PrinciPal.Common.Errors;

internal sealed class NoneError : ErrorBase
{
    public static readonly NoneError Instance = new();

    private NoneError() : base(string.Empty, string.Empty) { }
}
