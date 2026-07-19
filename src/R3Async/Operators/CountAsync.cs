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
        /// Subscribes to the source, consumes it to completion, and returns the number of values that satisfy <paramref name="predicate"/> (or all values, if <see langword="null"/>).
        /// </summary>
        /// <exception cref="OverflowException">More than <see cref="int.MaxValue"/> matching values were produced.</exception>
        public async ValueTask<int> CountAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var observer = new CountAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns the total number of values produced.
        /// </summary>
        /// <exception cref="OverflowException">More than <see cref="int.MaxValue"/> values were produced.</exception>
        public ValueTask<int> CountAsync(CancellationToken cancellationToken = default)
            => @this.CountAsync(null, cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="CountAsync(Func{T, bool}, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<int>> SubscribeCountAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new CountAsyncObserver<T>(predicate, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="CountAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<int>> SubscribeCountAsync(CancellationToken cancellationToken = default)
            => @this.SubscribeCountAsync(null, cancellationToken);
    }

    sealed class CountAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, int>(cancellationToken)
    {
        int _count;

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
