using System;
using System.Threading;
using System.Threading.Tasks;
namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> OnDispose(Func<ValueTask> onDispose)
        {
            return Create<T>((observer, token) =>
            {
                var newObserver = new OnDisposeObserver<T>(observer, onDispose);
                return @this.SubscribeAsync(newObserver, token);
            });
        }

        public AsyncObservable<T> OnDispose(Action onDispose)
        {
            return Create<T>((observer, token) =>
            {
                var newObserver = new OnDisposeObserverSync<T>(observer, onDispose);
                return @this.SubscribeAsync(newObserver, token);
            });
        }
    }

    sealed class OnDisposeObserverSync<T>(AsyncObserver<T> observer, Action finallySync) : AsyncObserver<T>
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

    class OnDisposeObserver<T>(AsyncObserver<T> observer, Func<ValueTask> finallyAsync) : AsyncObserver<T>
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
