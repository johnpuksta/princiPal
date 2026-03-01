using PrinciPal.Common.Abstractions;
using PrinciPal.Common.Errors;
using PrinciPal.Common.Results;

namespace PrinciPal.Common.Extensions;

public static class ResultExtensions
{
    extension(Result result)
    {
        /// <summary>
        /// Chains a follow-up operation that returns a Result. Short-circuits on failure.
        /// </summary>
        public Result Bind(Func<Result> next) =>
            result.IsSuccess ? next() : result;

        /// <summary>
        /// Chains a follow-up operation that returns a Result&lt;T&gt;. Short-circuits on failure.
        /// </summary>
        public Result<TOut> Bind<TOut>(Func<Result<TOut>> next) =>
            result.IsSuccess ? next() : Result<TOut>.Failure(result.Error);

        /// <summary>
        /// Executes a side-effect on success without changing the result (non-generic).
        /// </summary>
        public Result Tap(Action action)
        {
            if (result.IsSuccess)
                action();

            return result;
        }
    }

    extension<TIn>(Result<TIn> result)
    {
        /// <summary>
        /// Chains a follow-up operation on success value. Short-circuits on failure.
        /// </summary>
        public Result<TOut> Bind<TOut>(Func<TIn, Result<TOut>> next) =>
            result.IsSuccess ? next(result.Value) : Result<TOut>.Failure(result.Error);

        /// <summary>
        /// Chains a follow-up operation that returns a non-generic Result. Short-circuits on failure.
        /// </summary>
        public Result Bind(Func<TIn, Result> next) =>
            result.IsSuccess ? next(result.Value) : Result.Failure(result.Error);

        /// <summary>
        /// Transforms the success value. Short-circuits on failure.
        /// </summary>
        public Result<TOut> Map<TOut>(Func<TIn, TOut> map) =>
            result.IsSuccess ? Result<TOut>.Success(map(result.Value)) : Result<TOut>.Failure(result.Error);

        /// <summary>
        /// Validates the current result against a predicate. Returns failure if predicate fails.
        /// </summary>
        public Result<TIn> Ensure(Func<TIn, bool> predicate, IError error) =>
            result.IsSuccess && !predicate(result.Value)
                ? Result<TIn>.Failure(error)
                : result;

        /// <summary>
        /// Executes a side-effect on success without changing the result. Useful for logging.
        /// </summary>
        public Result<TIn> Tap(Action<TIn> action)
        {
            if (result.IsSuccess)
                action(result.Value);

            return result;
        }
    }
}
