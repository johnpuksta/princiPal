using PrinciPal.Common.Abstractions;
using PrinciPal.Common.Errors;

namespace PrinciPal.Common.Results;

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value) : base(true, NoneError.Instance)
    {
        _value = value;
    }

    private Result(IError error) : base(false, error)
    {
        _value = default;
    }

    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public static Result<TValue> Success(TValue value) => new(value);

    public new static Result<TValue> Failure(IError error) => new(error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<IError, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    public void Switch(Action<TValue> onSuccess, Action<IError> onFailure)
    {
        if (IsSuccess)
            onSuccess(Value);
        else
            onFailure(Error);
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(ErrorBase error) => Failure(error);
}
