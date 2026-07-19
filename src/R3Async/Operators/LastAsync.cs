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
        /// Subscribes to the source, consumes it to completion, and returns the last value that satisfies <paramref name="predicate"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence completes successfully without producing a matching value.</exception>
        public async ValueTask<T> LastAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            var observer = new LastAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns its last value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence completes successfully without producing any value.</exception>
        public async ValueTask<T> LastAsync(CancellationToken cancellationToken = default)
        {
            var observer = new LastAsyncObserver<T>(null, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established.
        /// Await <see cref="SubscriptionHandle{T}.GetValueAsync(TimeSpan?, CancellationToken)"/> on the handle to wait for the sequence to complete and obtain the last value that satisfies <paramref name="predicate"/>.
        /// Splits "subscribe" from "wait for the result" to avoid missing values that occur before subscribing (see <see cref="LastAsync(Func{T, bool}, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T>> SubscribeLastAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new LastAsyncObserver<T>(predicate, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established.
        /// Await <see cref="SubscriptionHandle{T}.GetValueAsync(TimeSpan?, CancellationToken)"/> on the handle to wait for the sequence to complete and obtain its last value.
        /// Splits "subscribe" from "wait for the result" to avoid missing values that occur before subscribing (see <see cref="LastAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T>> SubscribeLastAsync(CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new LastAsyncObserver<T>(null, cancellationToken), cancellationToken);
    }

    sealed class LastAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
    {
        bool _hasValue;
        T? _last;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                _hasValue = true;
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
            if (!result.IsSuccess)
            {
                return TrySetException(result.Exception);
            }

            if (_hasValue)
            {
                return TrySetCompleted(_last!);
            }

            var message = predicate is null ? "Sequence contains no elements." : "Sequence contains no matching elements.";
            return TrySetException(new InvalidOperationException(message));
        }
    }
}
