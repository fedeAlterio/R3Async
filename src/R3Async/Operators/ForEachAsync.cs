using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask ForEachAsync(Func<T, CancellationToken, ValueTask> onNextAsync,
                                           CancellationToken cancellationToken = default)
        {
            var observer = new ForEachObserver<T>(onNextAsync, cancellationToken);
            await @this.SubscribeAsync(observer, cancellationToken);
            await observer.WaitValueAsync();
        }

        public async ValueTask ForEachAsync(Action<T> onNext, CancellationToken cancellationToken = default)
        {
            if (onNext is null) throw new ArgumentNullException(nameof(onNext));
            var observer = new ForEachObserverSync<T>(onNext, cancellationToken);
            await @this.SubscribeAsync(observer, cancellationToken);
            await observer.WaitValueAsync();
        }
    }

    sealed class ForEachObserver<T>(Func<T, CancellationToken, ValueTask> onNextAsync, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            return onNextAsync(value, cancellationToken);
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return result.IsSuccess ? TrySetCompleted(true) : TrySetException(result.Exception);
        }
    }

    sealed class ForEachObserverSync<T>(Action<T> onNext, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        readonly Action<T> _onNext = onNext;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            _onNext(value);
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return result.IsSuccess ? TrySetCompleted(true) : TrySetException(result.Exception);
        }
    }
}
