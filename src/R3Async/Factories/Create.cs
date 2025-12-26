using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Create<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask<IAsyncDisposable>> subscribeAsync)
    {
        return subscribeAsync is null 
            ? throw new ArgumentNullException(nameof(subscribeAsync)) 
            : new AnonymousAsyncObservable<T>(subscribeAsync);
    }
}