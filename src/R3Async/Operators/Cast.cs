using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Casts each value of the source sequence to <typeparamref name="TResult"/>.
        /// </summary>
        /// <typeparam name="TResult">The type to cast values to.</typeparam>
        /// <remarks>If the cast fails for a value, the sequence completes with a failure result carrying the cast exception.</remarks>
        public AsyncObservable<TResult> Cast<TResult>()
        {
            return new CastObservable<T, TResult>(@this);
        }
    }
}

sealed class CastObservable<T, TResult>(AsyncObservable<T> source) : AsyncObservable<TResult>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TResult> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new CastObserver(observer), cancellationToken);
    }

    sealed class CastObserver(AsyncObserver<TResult> observer) : AsyncObserver<T>
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            try
            {
                var v = (TResult)(object?)value!;
                await observer.OnNextAsync(v, cancellationToken);
            }
            catch (Exception e)
            {
                await observer.OnCompletedAsync(Result.Failure(e));
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}
