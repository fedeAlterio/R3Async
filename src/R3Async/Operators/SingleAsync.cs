using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<T> SingleAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            var observer = new SingleAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public async ValueTask<T> SingleAsync(CancellationToken cancellationToken = default)
        {
            var observer = new SingleAsyncObserver<T>(null, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class SingleAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, T>(cancellationToken)
    {
        bool _hasValue;
        T? _value;

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

            if (!_hasValue)
            {
                var message = predicate is null ? "Sequence contains no elements." : "Sequence contains no matching elements.";
                return TrySetException(new InvalidOperationException(message));
            }

            return TrySetCompleted(_value!);
        }
    }
}
