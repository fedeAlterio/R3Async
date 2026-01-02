using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public async ValueTask<bool> ContainsAsync(T value, IEqualityComparer<T>? comparer, CancellationToken cancellationToken = default)
        {
            var cmp = comparer ?? EqualityComparer<T>.Default;
            var observer = new ContainsAsyncObserver<T>(value, cmp, cancellationToken);
            _ = await @this.SubscribeAsync(observer, cancellationToken);
            return await observer.WaitValueAsync();
        }

        public ValueTask<bool> ContainsAsync(T value, CancellationToken cancellationToken = default)
            => @this.ContainsAsync(value, null, cancellationToken);
    }

    sealed class ContainsAsyncObserver<T>(T value, IEqualityComparer<T> comparer, CancellationToken cancellationToken) : TaskAsyncObserverBase<T, bool>(cancellationToken)
    {
        protected override async ValueTask OnNextAsyncCore(T value1, CancellationToken cancellationToken)
        {
            if (comparer.Equals(value, value1))
            {
                await TrySetCompleted(true);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => TrySetException(error);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => result.IsSuccess ? TrySetCompleted(false) : TrySetException(result.Exception);
    }
}
