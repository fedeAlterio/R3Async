using System;
using System.Collections.Generic;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Filters out consecutive duplicate values, comparing each value only against the immediately preceding one, using the default equality comparer for <typeparamref name="T"/>.
        /// </summary>
        public AsyncObservable<T> DistinctUntilChanged() => @this.DistinctUntilChanged(EqualityComparer<T>.Default);

        /// <summary>
        /// Filters out consecutive duplicate values, comparing each value only against the immediately preceding one, using <paramref name="equalityComparer"/>.
        /// </summary>
        public AsyncObservable<T> DistinctUntilChanged(IEqualityComparer<T> equalityComparer)
        {
            if (equalityComparer is null)
                throw new ArgumentNullException(nameof(equalityComparer));

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

        /// <summary>
        /// Filters out consecutive values whose key (as produced by <paramref name="keySelector"/>) equals the key of the immediately preceding value, using the default equality comparer for <typeparamref name="TKey"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the key used for comparison.</typeparam>
        public AsyncObservable<T> DistinctUntilChangedBy<TKey>(Func<T, TKey> keySelector) => @this.DistinctUntilChangedBy(keySelector, EqualityComparer<TKey>.Default);

        /// <summary>
        /// Filters out consecutive values whose key (as produced by <paramref name="keySelector"/>) equals the key of the immediately preceding value, using <paramref name="equalityComparer"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the key used for comparison.</typeparam>
        public AsyncObservable<T> DistinctUntilChangedBy<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey> equalityComparer)
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (equalityComparer is null) throw new ArgumentNullException(nameof(equalityComparer));

            return Create<T>(async (observer, subscribeToken) =>
            {
                bool hasPrevious = false;
                TKey? previousKey = default;
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    var hadPrevious = hasPrevious;
                    var prev = previousKey;
                    var key = keySelector(x);
                    hasPrevious = true;
                    previousKey = key;
                    if (!hadPrevious || !equalityComparer.Equals(prev!, key))
                    {
                        await observer.OnNextAsync(x, token);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
