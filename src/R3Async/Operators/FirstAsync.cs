using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Subscribes to the source and returns the first value that satisfies <paramref name="predicate"/>, then unsubscribes.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence completes successfully without producing a matching value.</exception>
        public async ValueTask<T> FirstAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            var observer = new FirstAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source and returns its first value, then unsubscribes.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence completes successfully without producing any value.</exception>
        public async ValueTask<T> FirstAsync(CancellationToken cancellationToken = default)
        {
            var observer = new FirstAsyncObserver<T>(null, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }


        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, without waiting for a matching value.
        /// Await <see cref="SubscriptionHandle{T}.GetValueAsync(TimeSpan?, CancellationToken)"/> on the handle to obtain the first value that satisfies <paramref name="predicate"/>.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="FirstAsync(Func{T, bool}, CancellationToken)"/>, this splits "subscribe" from "wait for the result", eliminating the race window
        /// where a matching value could occur between deciding to subscribe and the subscription actually becoming active.
        /// </remarks>
        public ValueTask<SubscriptionHandle<T>> SubscribeFirstAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new FirstAsyncObserver<T>(predicate, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, without waiting for a value.
        /// Await <see cref="SubscriptionHandle{T}.GetValueAsync(TimeSpan?, CancellationToken)"/> on the handle to obtain the first value.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="FirstAsync(CancellationToken)"/>, this splits "subscribe" from "wait for the result", eliminating the race window
        /// where a value could arrive between deciding to subscribe and the subscription actually becoming active.
        /// </remarks>
        public ValueTask<SubscriptionHandle<T>> SubscribeFirstAsync(CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new FirstAsyncObserver<T>(null, cancellationToken), cancellationToken);
    }

    sealed class FirstAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                await TrySetCompleted(value);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            var exception = result.IsSuccess
                ? new InvalidOperationException(predicate is null ? "Sequence contains no elements." : "Sequence contains no matching elements.")
                : result.Exception;
            return TrySetException(exception);
        }
    }
}
