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
        /// Subscribes to the source, consumes it to completion, and returns the last value that satisfies <paramref name="predicate"/>, or <paramref name="defaultValue"/> if none matched.
        /// </summary>
        public async ValueTask<T?> LastOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new LastOrDefaultObserver<T>(predicate, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns its last value, or <see langword="default"/>(<typeparamref name="T"/>) if it produced no value.
        /// </summary>
        public ValueTask<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return @this.LastOrDefaultAsync(default, cancellationToken);
        }

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns its last value, or <paramref name="defaultValue"/> if it produced no value.
        /// </summary>
        public async ValueTask<T?> LastOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new LastOrDefaultObserver<T>(null, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="LastOrDefaultAsync(Func{T, bool}, T, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeLastOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new LastOrDefaultObserver<T>(predicate, defaultValue, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="LastOrDefaultAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeLastOrDefaultAsync(CancellationToken cancellationToken = default)
            => @this.SubscribeLastOrDefaultAsync(default, cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="LastOrDefaultAsync(T, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeLastOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new LastOrDefaultObserver<T>(null, defaultValue, cancellationToken), cancellationToken);
    }

    sealed class LastOrDefaultObserver<T>(Func<T, bool>? predicate, T? defaultValue, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T?>(cancellationToken)
    {
        T? _last = defaultValue;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                _last = value;
            }

            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return result.IsSuccess ? TrySetCompleted(_last!) : TrySetException(result.Exception);
        }
    }
}
