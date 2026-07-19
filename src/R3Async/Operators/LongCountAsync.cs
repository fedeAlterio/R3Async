using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns the number of values that satisfy <paramref name="predicate"/> (or all values, if <see langword="null"/>) as a <see cref="long"/>.
        /// </summary>
        public async ValueTask<long> LongCountAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var observer = new LongCountAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns the total number of values produced as a <see cref="long"/>.
        /// </summary>
        public ValueTask<long> LongCountAsync(CancellationToken cancellationToken = default)
            => @this.LongCountAsync(null, cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="LongCountAsync(Func{T, bool}, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<long>> SubscribeLongCountAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new LongCountAsyncObserver<T>(predicate, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="LongCountAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<long>> SubscribeLongCountAsync(CancellationToken cancellationToken = default)
            => @this.SubscribeLongCountAsync(null, cancellationToken);
    }

    sealed class LongCountAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, long>(cancellationToken)
    {
        long _count;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                _count = checked(_count + 1);
            }

            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(_count);
        }
    }
}
