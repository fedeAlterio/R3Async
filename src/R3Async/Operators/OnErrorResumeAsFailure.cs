using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> OnErrorResumeAsFailure<T>(this AsyncObservable<T> @this)
    {
        return new OnErroreResumeAsFailureObservable<T>(@this);
    }

    sealed class OnErroreResumeAsFailureObservable<T>(AsyncObservable<T> source) : AsyncObservable<T>
    {
        protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            return source.SubscribeAsync(new OnErroreResumeAsFailureObserver(observer), cancellationToken);
        }

        sealed class OnErroreResumeAsFailureObserver(AsyncObserver<T> observer) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                return observer.OnNextAsync(value, cancellationToken);
            }

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                return observer.OnCompletedAsync(Result.Failure(error));
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
            {
                return observer.OnCompletedAsync(result);
            }
        }
    }
}
