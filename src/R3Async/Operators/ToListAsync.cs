using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Subscribes to the source, consumes it to completion, and returns all produced values as a <see cref="List{T}"/>, in emission order.
        /// </summary>
        public async ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        {
            var observer = new ToListAsyncObserver<T>(cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        /// <summary>
        /// Eagerly subscribes to the source and returns a <see cref="SubscriptionHandle{T}"/> as soon as the subscription is established, splitting "subscribe" from
        /// "wait for the result" (see <see cref="ToListAsync(CancellationToken)"/> for the combined version).
        /// </summary>
        public ValueTask<SubscriptionHandle<List<T>>> SubscribeToListAsync(CancellationToken cancellationToken = default)
            => @this.ToSubscriptionAsyncHandleAsync(new ToListAsyncObserver<T>(cancellationToken), cancellationToken);
    }

    sealed class ToListAsyncObserver<T>(CancellationToken cancellationToken) : TaskAsyncObserverBase<T, List<T>>(cancellationToken)
    {
        readonly List<T> _items = new();

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            _items.Add(value);
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(_items);
        }
    }
}
