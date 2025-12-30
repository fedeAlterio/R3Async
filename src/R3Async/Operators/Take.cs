using System;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
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
