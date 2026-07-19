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
        /// Subscribes to the source and returns the first value that satisfies <paramref name="predicate"/>, or <paramref name="defaultValue"/> if the sequence completes successfully without a match.
        /// </summary>
        public async ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new FirstOrDefaultObserver<T>(predicate, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source and returns its first value, or <see langword="default"/>(<typeparamref name="T"/>) if the sequence completes successfully without producing any value.
        /// </summary>
        public ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return @this.FirstOrDefaultAsync(default, cancellationToken);
        }

        /// <summary>
        /// Subscribes to the source and returns its first value, or <paramref name="defaultValue"/> if the sequence completes successfully without producing any value.
        /// </summary>
        public async ValueTask<T?> FirstOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new FirstOrDefaultObserver<T>(null, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, without waiting for a matching value.
        /// Splits "subscribe" from "wait for the result" (see <see cref="FirstOrDefaultAsync(Func{T, bool}, T, CancellationToken)"/> for the combined version) to avoid missing a match that occurs before subscribing.
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeFirstOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new FirstOrDefaultObserver<T>(predicate, defaultValue, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, without waiting for a value.
        /// Splits "subscribe" from "wait for the result" (see <see cref="FirstOrDefaultAsync(CancellationToken)"/> for the combined version) to avoid missing a value that arrives before subscribing.
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeFirstOrDefaultAsync(CancellationToken cancellationToken = default)
            => @this.SubscribeFirstOrDefaultAsync(default, cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, without waiting for a value.
        /// Splits "subscribe" from "wait for the result" (see <see cref="FirstOrDefaultAsync(T, CancellationToken)"/> for the combined version) to avoid missing a value that arrives before subscribing.
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeFirstOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new FirstOrDefaultObserver<T>(null, defaultValue, cancellationToken), cancellationToken);
    }

    sealed class FirstOrDefaultObserver<T>(Func<T, bool>? predicate, T? defaultValue, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T?>(cancellationToken)
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
            return result.IsSuccess ? TrySetCompleted(defaultValue!) : TrySetException(result.Exception);
        }
    }
}
