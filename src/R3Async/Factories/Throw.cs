using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that emits no values and immediately completes with a failure
    /// result carrying <paramref name="error"/> upon subscription.
    /// </summary>
    public static AsyncObservable<T> Throw<T>(Exception error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));
        return new AsyncObservableThrow<T>(error);
    } 
    
    sealed class AsyncObservableThrow<T>(Exception error) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            await observer.OnCompletedAsync(Result.Failure(error));
            return AsyncDisposable.Empty;
        }
    }
}