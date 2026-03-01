namespace PrinciPal.Common.Abstractions;

public interface IError
{
    string Code { get; }
    string Description { get; }
}
