using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<TResult> Cast<TResult>()
        {
            return Create<TResult>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    try
                    {
                        var v = (TResult)(object?)x!;
                        await observer.OnNextAsync(v, token);
                    }
                    catch (Exception e)
                    {
                        await observer.OnCompletedAsync(Result.Failure(e));
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
