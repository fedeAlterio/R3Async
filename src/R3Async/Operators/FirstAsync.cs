using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<T> FirstAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            var observer = new FirstAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public async ValueTask<T> FirstAsync(CancellationToken cancellationToken = default)
        {
            var observer = new FirstAsyncObserver<T>(null, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class FirstAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
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
            var exception = result.IsSuccess
                ? new InvalidOperationException(predicate is null ? "Sequence contains no elements." : "Sequence contains no matching elements.")
                : result.Exception;
            return TrySetException(exception);
        }
    }
}
