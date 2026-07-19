using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Forces downstream observer calls to yield onto the current <see cref="AsyncContext"/> instead of continuing synchronously on the emitting call stack.
    /// </summary>
    public static AsyncObservable<T> Yield<T>(this AsyncObservable<T> @this) => new YieldObservable<T>(@this);

    sealed class YieldObservable<T>(AsyncObservable<T> source) : AsyncObservable<T>
    {
        protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var currentContext = AsyncContext.GetCurrent();
            return source.SubscribeAsync(new ObserveOnAsyncObservable<T>.ObserveOnObserver(observer, currentContext, true), cancellationToken);
        }
    }
}
