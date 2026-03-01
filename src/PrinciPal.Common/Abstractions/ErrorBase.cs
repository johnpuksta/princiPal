namespace PrinciPal.Common.Abstractions;

public abstract class ErrorBase : IError, IEquatable<ErrorBase>
{
    protected ErrorBase(string code, string description)
    {
        Code = code;
        Description = description;
    }

    public string Code { get; }
    public string Description { get; }

    public bool Equals(ErrorBase? other) =>
        other is not null && Code == other.Code && GetType() == other.GetType();

    public override bool Equals(object? obj) =>
        obj is ErrorBase other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Code.GetHashCode() * 397) ^ GetType().GetHashCode();
        }
    }

    public static bool operator ==(ErrorBase? left, ErrorBase? right) =>
        Equals(left, right);

    public static bool operator !=(ErrorBase? left, ErrorBase? right) =>
        !Equals(left, right);

    public override string ToString() => $"[{GetType().Name}] {Code}: {Description}";
}
