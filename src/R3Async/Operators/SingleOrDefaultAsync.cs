using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<T?> SingleOrDefaultAsync(Func<T, bool> predicate, T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new SingleOrDefaultObserver<T>(predicate, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return @this.SingleOrDefaultAsync(default, cancellationToken);
        }

        public async ValueTask<T?> SingleOrDefaultAsync(T? defaultValue, CancellationToken cancellationToken = default)
        {
            var observer = new SingleOrDefaultObserver<T>(null, defaultValue, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class SingleOrDefaultObserver<T>(Func<T, bool>? predicate, T? defaultValue, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
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
