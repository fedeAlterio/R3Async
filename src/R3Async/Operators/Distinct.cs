using System;
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

        public AsyncObservable<T> DistinctBy<TKey>(Func<T, TKey> keySelector) => @this.DistinctBy(keySelector, EqualityComparer<TKey>.Default);

        public AsyncObservable<T> DistinctBy<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey> equalityComparer)
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (equalityComparer is null) throw new ArgumentNullException(nameof(equalityComparer));

            return Create<T>(async (observer, subscribeToken) =>
            {
                var seen = new HashSet<TKey>(equalityComparer);
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    var key = keySelector(x);
                    if (seen.Add(key))
                    {
                        await observer.OnNextAsync(x, token);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
