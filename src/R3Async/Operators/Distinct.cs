using System.Collections.Generic;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> Distinct() => @this.Distinct(EqualityComparer<T>.Default);

        public AsyncObservable<T> Distinct(IEqualityComparer<T> equalityComparer)
        {
            return Create<T>(async (observer, subscribeToken) =>
            {
                var seen = new HashSet<T>(equalityComparer);
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    if (seen.Add(x))
                    {
                        await observer.OnNextAsync(x, token);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
