using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> ObserveOn<T>(this AsyncObservable<T> @this, AsyncContext asyncContext, bool forceYielding = false)
    {
        return new ObserveOnAsyncObservable<T>(@this, asyncContext, forceYielding);
    }
}

internal sealed class ObserveOnAsyncObservable<T>(AsyncObservable<T> source, AsyncContext asyncContext, bool forceYielding) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var observeOnObserver = new ObserveOnObserver(observer, asyncContext, forceYielding);
        return await source.SubscribeAsync(observeOnObserver, cancellationToken);
    }

    sealed class ObserveOnObserver(AsyncObserver<T> observer, AsyncContext asyncContext, bool forceYielding) : AsyncObserver<T>
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            await asyncContext.SwitchContextAsync(forceYielding, cancellationToken);
            await observer.OnNextAsync(value, cancellationToken);
        }

        protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            await asyncContext.SwitchContextAsync(forceYielding, cancellationToken);
            await observer.OnErrorResumeAsync(error, cancellationToken);
        }

        protected override async ValueTask OnCompletedAsyncCore(Result result)
        {
            await asyncContext.SwitchContextAsync(forceYielding, CancellationToken.None);
            await observer.OnCompletedAsync(result);
        }
    }
}
