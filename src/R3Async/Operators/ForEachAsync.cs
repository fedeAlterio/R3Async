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
        /// Subscribes to the source and awaits <paramref name="onNextAsync"/> for each value, in order, until the sequence completes.
        /// </summary>
        public async ValueTask ForEachAsync(Func<T, CancellationToken, ValueTask> onNextAsync,
                                           CancellationToken cancellationToken = default)
        {
            var observer = new ForEachObserver<T>(onNextAsync, cancellationToken);
            await @this.SubscribeAsync(observer, cancellationToken);
            await observer.WaitValueAsync();
        }

        /// <summary>
        /// Subscribes to the source and invokes <paramref name="onNext"/> for each value, in order, until the sequence completes.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="onNext"/> is <see langword="null"/>.</exception>
        public async ValueTask ForEachAsync(Action<T> onNext, CancellationToken cancellationToken = default)
        {
            if (onNext is null) throw new ArgumentNullException(nameof(onNext));
            var observer = new ForEachObserverSync<T>(onNext, cancellationToken);
            await @this.SubscribeAsync(observer, cancellationToken);
            await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="ForEachAsync(Func{T, CancellationToken, ValueTask}, CancellationToken)"/> for the combined version).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="onNextAsync"/> is <see langword="null"/>.</exception>
        public ValueTask<SubscriptionHandle<bool>> SubscribeForEachAsync(Func<T, CancellationToken, ValueTask> onNextAsync, CancellationToken cancellationToken = default)
        {
            if (onNextAsync is null) throw new ArgumentNullException(nameof(onNextAsync));
            return @this.ToSubscriptionAsyncHandleAsync(new ForEachObserver<T>(onNextAsync, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="ForEachAsync(Action{T}, CancellationToken)"/> for the combined version).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="onNext"/> is <see langword="null"/>.</exception>
        public ValueTask<SubscriptionHandle<bool>> SubscribeForEachAsync(Action<T> onNext, CancellationToken cancellationToken = default)
        {
            if (onNext is null) throw new ArgumentNullException(nameof(onNext));
            return @this.ToSubscriptionAsyncHandleAsync(new ForEachObserverSync<T>(onNext, cancellationToken), cancellationToken);
        }
    }

    sealed class ForEachObserver<T>(Func<T, CancellationToken, ValueTask> onNextAsync, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            return onNextAsync(value, cancellationToken);
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return result.IsSuccess ? TrySetCompleted(true) : TrySetException(result.Exception);
        }
    }

    sealed class ForEachObserverSync<T>(Action<T> onNext, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        readonly Action<T> _onNext = onNext;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            _onNext(value);
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return result.IsSuccess ? TrySetCompleted(true) : TrySetException(result.Exception);
        }
    }
}
