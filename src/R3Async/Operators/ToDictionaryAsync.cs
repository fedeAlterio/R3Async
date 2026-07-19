using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns a <see cref="Dictionary{TKey, TValue}"/> mapping each value's key (as produced by <paramref name="keySelector"/>) to the value itself.
        /// </summary>
        /// <typeparam name="TKey">The type of the dictionary key.</typeparam>
        /// <param name="comparer">Equality comparer used for keys, or <see langword="null"/> for the default comparer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Two values produce the same key.</exception>
        public async ValueTask<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey>? comparer = null, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            var observer = new ToDictionaryAsyncObserver<T, TKey, T>(keySelector, x => x, comparer, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns a <see cref="Dictionary{TKey, TValue}"/> mapping each value's key (as produced by <paramref name="keySelector"/>) to a projected element (as produced by <paramref name="elementSelector"/>).
        /// </summary>
        /// <typeparam name="TKey">The type of the dictionary key.</typeparam>
        /// <typeparam name="TValue">The type of the dictionary value.</typeparam>
        /// <param name="comparer">Equality comparer used for keys, or <see langword="null"/> for the default comparer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> or <paramref name="elementSelector"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Two values produce the same key.</exception>
        public async ValueTask<Dictionary<TKey, TValue>> ToDictionaryAsync<TKey, TValue>(Func<T, TKey> keySelector, Func<T, TValue> elementSelector, IEqualityComparer<TKey>? comparer = null, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (elementSelector is null) throw new ArgumentNullException(nameof(elementSelector));
            var observer = new ToDictionaryAsyncObserver<T, TKey, TValue>(keySelector, elementSelector, comparer, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="ToDictionaryAsync{TKey}(Func{T, TKey}, IEqualityComparer{TKey}, CancellationToken)"/> for the combined version).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
        public ValueTask<SubscriptionHandle<Dictionary<TKey, T>>> SubscribeToDictionaryAsync<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey>? comparer = null, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            return @this.ToSubscriptionAsyncHandleAsync(new ToDictionaryAsyncObserver<T, TKey, T>(keySelector, x => x, comparer, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="ToDictionaryAsync{TKey, TValue}(Func{T, TKey}, Func{T, TValue}, IEqualityComparer{TKey}, CancellationToken)"/> for the combined version).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> or <paramref name="elementSelector"/> is <see langword="null"/>.</exception>
        public ValueTask<SubscriptionHandle<Dictionary<TKey, TValue>>> SubscribeToDictionaryAsync<TKey, TValue>(Func<T, TKey> keySelector, Func<T, TValue> elementSelector, IEqualityComparer<TKey>? comparer = null, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (elementSelector is null) throw new ArgumentNullException(nameof(elementSelector));
            return @this.ToSubscriptionAsyncHandleAsync(new ToDictionaryAsyncObserver<T, TKey, TValue>(keySelector, elementSelector, comparer, cancellationToken), cancellationToken);
        }
    }

    sealed class ToDictionaryAsyncObserver<TSource, TKey, TValue>(Func<TSource, TKey> keySelector, Func<TSource, TValue> elementSelector, IEqualityComparer<TKey>? comparer, CancellationToken cancellationToken) : TaskAsyncObserverBase<TSource, Dictionary<TKey, TValue>>(cancellationToken)
        where TKey : notnull
    {
        readonly Dictionary<TKey, TValue> _map = comparer is null ? new() : new(comparer);

        protected override ValueTask OnNextAsyncCore(TSource value, CancellationToken cancellationToken)
        {
            var key = keySelector(value);
            _map.Add(key, elementSelector(value));
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => TrySetException(error);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(_map);
    }
}
