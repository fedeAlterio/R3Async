using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<int> CountAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var observer = new CountAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<int> CountAsync(CancellationToken cancellationToken = default)
            => @this.CountAsync(null, cancellationToken);
    }

    sealed class CountAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, int>(cancellationToken)
    {
        int _count;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                _count = checked(_count + 1);
            }

            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(_count);
        }
    }
}
