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
        /// Subscribes to the source and returns whether any value satisfies <paramref name="predicate"/>, unsubscribing as soon as a match is found (or the sequence completes).
        /// </summary>
        /// <param name="predicate">Predicate to test each value against, or <see langword="null"/> to check whether the sequence produces any value at all.</param>
        public async ValueTask<bool> AnyAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var observer = new AnyAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source and returns whether it produces any value, unsubscribing as soon as the first value arrives (or the sequence completes).
        /// </summary>
        public ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default)
            => @this.AnyAsync(null, cancellationToken);

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns whether every value satisfies <paramref name="predicate"/>, unsubscribing early as soon as a non-matching value is found.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
        public async ValueTask<bool> AllAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            var observer = new AllAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="AnyAsync(Func{T, bool}, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<bool>> SubscribeAnyAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new AnyAsyncObserver<T>(predicate, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="AnyAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<bool>> SubscribeAnyAsync(CancellationToken cancellationToken = default)
            => @this.SubscribeAnyAsync(null, cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="AllAsync(Func{T, bool}, CancellationToken)"/> for the combined version).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
        public ValueTask<SubscriptionHandle<bool>> SubscribeAllAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            return @this.ToSubscriptionAsyncHandleAsync(new AllAsyncObserver<T>(predicate, cancellationToken), cancellationToken);
        }
    }

    sealed class AnyAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                await TrySetCompleted(true);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(false);
        }
    }

    sealed class AllAsyncObserver<T>(Func<T, bool> predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        readonly Func<T, bool> _predicate = predicate;

        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (!_predicate(value))
            {
                await TrySetCompleted(false);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(true);
        }
    }
}
