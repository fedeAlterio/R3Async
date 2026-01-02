using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<T?> LastOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new LastOrDefaultObserver<T>(predicate, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return @this.LastOrDefaultAsync(default, cancellationToken);
        }

        public async ValueTask<T?> LastOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new LastOrDefaultObserver<T>(null, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class LastOrDefaultObserver<T>(Func<T, bool>? predicate, T? defaultValue, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
    {
        T? _last = defaultValue;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
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
            return result.IsSuccess ? TrySetCompleted(_last!) : TrySetException(result.Exception);
        }
    }
}
