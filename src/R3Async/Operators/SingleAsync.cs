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
        /// Subscribes to the source and returns the single value that satisfies <paramref name="predicate"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">No value matched, or more than one value matched.</exception>
        public async ValueTask<T> SingleAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            var observer = new SingleAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source and returns its single value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence produced no value, or more than one value.</exception>
        public async ValueTask<T> SingleAsync(CancellationToken cancellationToken = default)
        {
            var observer = new SingleAsyncObserver<T>(null, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="SingleAsync(Func{T, bool}, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T>> SubscribeSingleAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new SingleAsyncObserver<T>(predicate, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="SingleAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T>> SubscribeSingleAsync(CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new SingleAsyncObserver<T>(null, cancellationToken), cancellationToken);
    }

    sealed class SingleAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
    {
        bool _hasValue;
        T? _value;

        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                if (_hasValue)
                {
                    var message = predicate is null ? "Sequence contains more than one element." : "Sequence contains more than one matching element.";
                    await TrySetException(new InvalidOperationException(message));
                }
                else
                {
                    _hasValue = true;
                    _value = value;
                }
            }
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

            if (!_hasValue)
            {
                var message = predicate is null ? "Sequence contains no elements." : "Sequence contains no matching elements.";
                return TrySetException(new InvalidOperationException(message));
            }

            return TrySetCompleted(_value!);
        }
    }
}
