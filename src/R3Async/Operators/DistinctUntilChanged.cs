using System.Collections.Generic;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> DistinctUntilChanged() => @this.DistinctUntilChanged(EqualityComparer<T>.Default);

        public AsyncObservable<T> DistinctUntilChanged(IEqualityComparer<T> equalityComparer)
        {
            return Create<T>(async (observer, subscribeToken) =>
            {
                bool hasPrevious = false;
                T? previous = default;
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    var hadPrevious = hasPrevious;
                    var previousValue = previous;
                    hasPrevious = true;
                    previous = x;
                    if (!hadPrevious || !equalityComparer.Equals(previousValue!, x))
                    {
                        await observer.OnNextAsync(x, token);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
