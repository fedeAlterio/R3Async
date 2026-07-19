using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Filters the source sequence, forwarding only the values for which the async <paramref name="predicate"/> returns <see langword="true"/>.
        /// </summary>
        /// <param name="predicate">Async predicate evaluated for each value; values are dropped while it is awaited unless they pass.</param>
        public AsyncObservable<T> Where(Func<T, CancellationToken, ValueTask<bool>> predicate)
        {
            return Create<T>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    if (await predicate(x, token))
                    {
                        await observer.OnNextAsync(x, token);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }

        /// <summary>
        /// Filters the source sequence, forwarding only the values for which <paramref name="predicate"/> returns <see langword="true"/>.
        /// </summary>
        /// <param name="predicate">Synchronous predicate evaluated for each value.</param>
        public AsyncObservable<T> Where(Func<T, bool> predicate)
        {
            return Create<T>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync((x, token) =>
                {
                    if (predicate(x))
                    {
                        return observer.OnNextAsync(x, token);
                    }

                    return default;
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}