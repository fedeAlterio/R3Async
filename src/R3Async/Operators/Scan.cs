using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Applies an async accumulator function over the source sequence, emitting the running accumulated value for each source value.
        /// </summary>
        /// <typeparam name="TAcc">The type of the accumulated value.</typeparam>
        /// <param name="seed">The initial accumulator value.</param>
        /// <param name="accumulator">Async function combining the current accumulated value with the next source value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="accumulator"/> is <see langword="null"/>.</exception>
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

        /// <summary>
        /// Applies a synchronous accumulator function over the source sequence, emitting the running accumulated value for each source value.
        /// </summary>
        /// <typeparam name="TAcc">The type of the accumulated value.</typeparam>
        /// <param name="seed">The initial accumulator value.</param>
        /// <param name="accumulator">Function combining the current accumulated value with the next source value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="accumulator"/> is <see langword="null"/>.</exception>
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
