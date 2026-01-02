using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new FirstOrDefaultObserver<T>(predicate, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return @this.FirstOrDefaultAsync(default, cancellationToken);
        }

        public async ValueTask<T?> FirstOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new FirstOrDefaultObserver<T>(null, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class FirstOrDefaultObserver<T>(Func<T, bool>? predicate, T? defaultValue, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
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
            return result.IsSuccess ? TrySetCompleted(defaultValue!) : TrySetException(result.Exception);
        }
    }
}
