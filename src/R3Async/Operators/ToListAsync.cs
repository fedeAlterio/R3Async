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
        public async ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        {
            var observer = new ToListAsyncObserver<T>(cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
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
