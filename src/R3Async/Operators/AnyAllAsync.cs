using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<bool> AnyAsync(Func<T, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var observer = new AnyAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default)
            => @this.AnyAsync(null, cancellationToken);

        public async ValueTask<bool> AllAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            var observer = new AllAsyncObserver<T>(predicate, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }
    }

    sealed class AnyAsyncObserver<T>(Func<T, bool>? predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        bool _hasMatch;

        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (predicate is null || predicate(value))
            {
                _hasMatch = true;
                await TrySetCompleted(true);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(false);
        }
    }

    sealed class AllAsyncObserver<T>(Func<T, bool> predicate, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        readonly Func<T, bool> _predicate = predicate;

        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (!_predicate(value))
            {
                await TrySetCompleted(false);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            return TrySetException(error);
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            return !result.IsSuccess ? TrySetException(result.Exception) : TrySetCompleted(true);
        }
    }
}
