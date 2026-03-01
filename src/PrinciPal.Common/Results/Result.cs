using PrinciPal.Common.Abstractions;
using PrinciPal.Common.Errors;

namespace PrinciPal.Common.Results;

public class Result
{
    protected Result(bool isSuccess, IError error)
    {
        if (isSuccess && error is not NoneError)
            throw new ArgumentException("Success result cannot have an error.", nameof(error));

        if (!isSuccess && error is NoneError)
            throw new ArgumentException("Failure result must have an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IError Error { get; }

    public static Result Success() => new(true, NoneError.Instance);

    public static Result Failure(IError error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) =>
        Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(IError error) =>
        Result<TValue>.Failure(error);

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<IError, TResult> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Error);

    public void Switch(Action onSuccess, Action<IError> onFailure)
    {
        if (IsSuccess)
            onSuccess();
        else
            onFailure(Error);
    }
}
