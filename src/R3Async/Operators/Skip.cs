using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> Skip(int count)
        {
            return count switch
            {
                < 0 => throw new ArgumentOutOfRangeException(nameof(count)),
                0 => @this,
                _ => Create<T>(async (observer, subscribeToken) =>
                {
                    var remaining = count;

                    return await @this.SubscribeAsync(async (x, token) =>
                    {
                        if (remaining > 0)
                        {
                            remaining--;
                            return;
                        }

                        await observer.OnNextAsync(x, token);
                    }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
                })
            };
        }
    }
}
