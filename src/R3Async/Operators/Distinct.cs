using System;
using System.Collections.Generic;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Filters out values that have already been observed, using the default equality comparer for <typeparamref name="T"/>.
        /// </summary>
        public AsyncObservable<T> Distinct() => @this.Distinct(EqualityComparer<T>.Default);

        /// <summary>
        /// Filters out values that have already been observed, using <paramref name="equalityComparer"/> to compare values.
        /// </summary>
        /// <remarks>All previously seen values are kept in memory for the lifetime of the subscription.</remarks>
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

        /// <summary>
        /// Filters out values whose key (as produced by <paramref name="keySelector"/>) has already been observed, using the default equality comparer for <typeparamref name="TKey"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the key used to determine distinctness.</typeparam>
        public AsyncObservable<T> DistinctBy<TKey>(Func<T, TKey> keySelector) => @this.DistinctBy(keySelector, EqualityComparer<TKey>.Default);

        /// <summary>
        /// Filters out values whose key (as produced by <paramref name="keySelector"/>) has already been observed, using <paramref name="equalityComparer"/> to compare keys.
        /// </summary>
        /// <typeparam name="TKey">The type of the key used to determine distinctness.</typeparam>
        /// <remarks>All previously seen keys are kept in memory for the lifetime of the subscription.</remarks>
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
