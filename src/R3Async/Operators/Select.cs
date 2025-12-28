using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<TDest> Select<TDest>(Func<T, CancellationToken, ValueTask<TDest>> selector)
        {
            return R3Async.AsyncObservable.Create<TDest>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    var mapped = await selector(x, token);
                    await observer.OnNextAsync(mapped, token);  
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }

        public AsyncObservable<TDest> Select<TDest>(Func<T, TDest> selector)
        {
            return R3Async.AsyncObservable.Create<TDest>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync((x, token) =>
                {
                    var mapped = selector(x);
                    return observer.OnNextAsync(mapped, token);
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}