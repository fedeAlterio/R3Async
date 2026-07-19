using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Converts every <c>OnErrorResumeAsync</c> notification from <paramref name="@this"/> into a terminal failure
    /// completion (<see cref="Result.Failure(Exception)"/>), turning resumable errors into stream-ending ones.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by <paramref name="@this"/>.</typeparam>
    /// <param name="this">The source observable whose resumable errors should become terminal.</param>
    public static AsyncObservable<T> OnErrorResumeAsFailure<T>(this AsyncObservable<T> @this)
    {
        return new OnErrorResumeAsFailureObservable<T>(@this);
    }

    sealed class OnErrorResumeAsFailureObservable<T>(AsyncObservable<T> source) : AsyncObservable<T>
    {
        protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            return source.SubscribeAsync(new OnErrorResumeAsFailureObserver(observer), cancellationToken);
        }

        sealed class OnErrorResumeAsFailureObserver(AsyncObserver<T> observer) : AsyncObserver<T>
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
