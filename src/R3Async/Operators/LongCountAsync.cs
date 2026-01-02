using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<long> LongCountAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var observer = new LongCountAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<long> LongCountAsync(CancellationToken cancellationToken = default)
            => @this.LongCountAsync(null, cancellationToken);
    }

    sealed class LongCountAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, long>(cancellationToken)
    {
        long _count;

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
