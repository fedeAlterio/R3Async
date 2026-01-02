using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<T> LastAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            var observer = new LastAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public async ValueTask<T> LastAsync(CancellationToken cancellationToken = default)
        {
            var observer = new LastAsyncObserver<T>(null, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class LastAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
    {
        bool _hasValue;
        T? _last;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                _hasValue = true;
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
            if (!result.IsSuccess)
            {
                return TrySetException(result.Exception);
            }

            if (_hasValue)
            {
                return TrySetCompleted(_last!);
            }

            var message = predicate is null ? "Sequence contains no elements." : "Sequence contains no matching elements.";
            return TrySetException(new InvalidOperationException(message));
        }
    }
}
