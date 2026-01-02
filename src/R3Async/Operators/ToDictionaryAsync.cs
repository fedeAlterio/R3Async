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
        public async ValueTask<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey>? comparer = null, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            var observer = new ToDictionaryAsyncObserver<T, TKey, T>(keySelector, x => x, comparer, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public async ValueTask<Dictionary<TKey, TValue>> ToDictionaryAsync<TKey, TValue>(Func<T, TKey> keySelector, Func<T, TValue> elementSelector, IEqualityComparer<TKey>? comparer = null, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (elementSelector is null) throw new ArgumentNullException(nameof(elementSelector));
            var observer = new ToDictionaryAsyncObserver<T, TKey, TValue>(keySelector, elementSelector, comparer, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class ToDictionaryAsyncObserver<TSource, TKey, TValue>(Func<TSource, TKey> keySelector, Func<TSource, TValue> elementSelector, IEqualityComparer<TKey>? comparer, CancellationToken cancellationToken) : TaskAsyncObserverBase<TSource, Dictionary<TKey, TValue>>(cancellationToken)
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
