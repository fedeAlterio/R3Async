using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Projects each value of the source sequence into a new form using an async <paramref name="selector"/>.
        /// </summary>
        /// <typeparam name="TDest">The type produced by <paramref name="selector"/>.</typeparam>
        /// <param name="selector">Async projection function applied to each value.</param>
        public AsyncObservable<TDest> Select<TDest>(Func<T, CancellationToken, ValueTask<TDest>> selector)
        {
            return Create<TDest>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    var mapped = await selector(x, token);
                    await observer.OnNextAsync(mapped, token);  
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }

        /// <summary>
        /// Projects each value of the source sequence into a new form using a synchronous <paramref name="selector"/>.
        /// </summary>
        /// <typeparam name="TDest">The type produced by <paramref name="selector"/>.</typeparam>
        /// <param name="selector">Synchronous projection function applied to each value.</param>
        public AsyncObservable<TDest> Select<TDest>(Func<T, TDest> selector)
        {
            return Create<TDest>(async (observer, subscribeToken) =>
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