using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
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