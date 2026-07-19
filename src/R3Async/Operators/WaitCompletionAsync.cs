using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Subscribes to the source, discards its values, and completes once the source completes. Throws if the source completes with a failure result.
    /// </summary>
    public static async ValueTask WaitCompletionAsync<T>(this AsyncObservable<T> @this, CancellationToken cancellationToken = default)
    {
        var observer = new WaitCompletionAsyncObserver<T>(cancellationToken);
        _ = await @this.SubscribeAsync(observer, cancellationToken);
        await observer.WaitValueAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
    /// "wait for the result" (see <see cref="WaitCompletionAsync{T}"/> for the combined version).
    /// </summary>
    public static ValueTask<SubscriptionHandle<object?>> SubscribeWaitCompletionAsync<T>(this AsyncObservable<T> @this, CancellationToken cancellationToken = default)
        => @this.ToSubscriptionAsyncHandleAsync(new WaitCompletionAsyncObserver<T>(cancellationToken), cancellationToken);

    sealed class WaitCompletionAsyncObserver<T>(CancellationToken cancellationToken) : TaskAsyncObserverBase<T, object?>(cancellationToken)
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            return default;
        }
        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }
        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(null);
        }
    }
}
