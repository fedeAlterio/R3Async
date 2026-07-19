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
        /// Subscribes to the source and returns the single value that satisfies <paramref name="predicate"/>, or <paramref name="defaultValue"/> if none matched.
        /// </summary>
        /// <exception cref="InvalidOperationException">More than one value matched <paramref name="predicate"/>.</exception>
        public async ValueTask<T?> SingleOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new SingleOrDefaultObserver<T>(predicate, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source and returns its single value, or <see langword="default"/>(<typeparamref name="T"/>) if it produced no value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence produced more than one value.</exception>
        public ValueTask<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return @this.SingleOrDefaultAsync(default, cancellationToken);
        }

        /// <summary>
        /// Subscribes to the source and returns its single value, or <paramref name="defaultValue"/> if it produced no value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence produced more than one value.</exception>
        public async ValueTask<T?> SingleOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new SingleOrDefaultObserver<T>(null, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="SingleOrDefaultAsync(Func{T, bool}, T, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeSingleOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new SingleOrDefaultObserver<T>(predicate, defaultValue, cancellationToken), cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="SingleOrDefaultAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeSingleOrDefaultAsync(CancellationToken cancellationToken = default)
            => @this.SubscribeSingleOrDefaultAsync(default, cancellationToken);

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="SingleOrDefaultAsync(T, CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<T?>> SubscribeSingleOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new SingleOrDefaultObserver<T>(null, defaultValue, cancellationToken), cancellationToken);
    }

    sealed class SingleOrDefaultObserver<T>(Func<T, bool>? predicate, T? defaultValue, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T?>(cancellationToken)
    {
        bool _hasValue;
        T? _value = defaultValue;

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

            return TrySetCompleted(_value!);
        }
    }
}
