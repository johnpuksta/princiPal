using System.Threading.Tasks;
using PrinciPal.Common.Abstractions;
using PrinciPal.Common.Errors;
using PrinciPal.Common.Results;

namespace PrinciPal.Common.Extensions;

public static class ResultAsyncExtensions
{
    extension(Task<Result> resultTask)
    {
        public async Task<Result> Bind(Func<Result> next)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(next);
        }

        public async Task<Result<TOut>> Bind<TOut>(Func<Result<TOut>> next)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(next);
        }

        public async Task<TResult> Match<TResult>(
            Func<TResult> onSuccess,
            Func<IError, TResult> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(onSuccess, onFailure);
        }
    }

    extension<TIn>(Task<Result<TIn>> resultTask)
    {
        public async Task<Result<TOut>> Bind<TOut>(Func<TIn, Result<TOut>> next)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(next);
        }

        public async Task<Result<TOut>> Bind<TOut>(Func<TIn, Task<Result<TOut>>> next)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess
                ? await next(result.Value).ConfigureAwait(false)
                : Result<TOut>.Failure(result.Error);
        }

        public async Task<Result<TOut>> Map<TOut>(Func<TIn, TOut> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Map(map);
        }

        public async Task<Result<TIn>> Ensure(Func<TIn, bool> predicate, IError error)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Ensure(predicate, error);
        }

        public async Task<Result<TIn>> Tap(Action<TIn> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Tap(action);
        }

        public async Task<TResult> Match<TResult>(
            Func<TIn, TResult> onSuccess,
            Func<IError, TResult> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(onSuccess, onFailure);
        }
    }

    extension<TIn>(Result<TIn> result)
    {
        public async Task<Result<TOut>> Bind<TOut>(Func<TIn, Task<Result<TOut>>> next)
        {
            return result.IsSuccess
                ? await next(result.Value).ConfigureAwait(false)
                : Result<TOut>.Failure(result.Error);
        }
    }
}
