using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<TAcc> Scan<TAcc>(TAcc seed, Func<TAcc, T, CancellationToken, ValueTask<TAcc>> accumulator)
        {
            if (accumulator is null) throw new ArgumentNullException(nameof(accumulator));

            return Create<TAcc>(async (observer, subscribeToken) =>
            {
                var acc = seed;
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    acc = await accumulator(acc, x, token);
                    await observer.OnNextAsync(acc, token);
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }

        public AsyncObservable<TAcc> Scan<TAcc>(TAcc seed, Func<TAcc, T, TAcc> accumulator)
        {
            if (accumulator is null) throw new ArgumentNullException(nameof(accumulator));

            return Create<TAcc>(async (observer, subscribeToken) =>
            {
                var acc = seed;
                return await @this.SubscribeAsync((x, token) =>
                {
                    acc = accumulator(acc, x);
                    return observer.OnNextAsync(acc, token);
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
