using System;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Forwards only the first <paramref name="count"/> values from the source sequence, then completes successfully and unsubscribes.
        /// </summary>
        /// <param name="count">The maximum number of values to take. Must be non-negative; if zero, the result completes immediately without subscribing.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        public AsyncObservable<T> Take(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            return Create<T>(async (observer, subscribeToken) =>
            {
                if (count == 0)
                {
                    await observer.OnCompletedAsync(Result.Success);
                    return AsyncDisposable.Empty;
                }

                var remaining = count;

                return await @this.SubscribeAsync(async (x, token) =>
                {
                    remaining--;
                    await observer.OnNextAsync(x, token);

                    if (remaining == 0)
                    {
                        await observer.OnCompletedAsync(Result.Success);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
