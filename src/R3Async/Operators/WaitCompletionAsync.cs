using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static async ValueTask WaitCompletionAsync<T>(this AsyncObservable<T> @this, CancellationToken cancellationToken = default)
    {
        var observer = new WaitCompletionAsyncObserver<T>(cancellationToken);
        _ = await @this.SubscribeAsync(observer, cancellationToken);
        await observer.WaitValueAsync();
    }

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
