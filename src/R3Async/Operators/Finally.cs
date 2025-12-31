using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> Finally(Func<ValueTask> onDispose)
        {
            return Create<T>((observer, token) =>
            {
                var newObserver = new FinallyObserver<T>(observer, onDispose);
                return @this.SubscribeAsync(newObserver, token);
            });
        }

        public AsyncObservable<T> Finally(Action onDispose)
        {
            return Create<T>((observer, token) =>
            {
                var newObserver = new FinallyObserverSync<T>(observer, onDispose);
                return @this.SubscribeAsync(newObserver, token);
            });
        }
    }

    sealed class FinallyObserverSync<T>(AsyncObserver<T> observer, Action finallySync) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            => observer.OnNextAsync(value, cancellationToken);

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);

        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                finallySync();
            }
            finally
            {
                await base.DisposeAsyncCore();
            }
        }
    }

    class FinallyObserver<T>(AsyncObserver<T> observer, Func<ValueTask> finallyAsync) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            => observer.OnNextAsync(value, cancellationToken);

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);

        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await finallyAsync();
            }
            finally
            {
                await base.DisposeAsyncCore();
            }
        }
    }
}
