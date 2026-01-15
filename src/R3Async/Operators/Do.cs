using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> Do(Func<T, CancellationToken, ValueTask>? onNext, 
                                     Func<Exception, CancellationToken, ValueTask>? onErrorResume = null,
                                     Func<Result, ValueTask>? onCompleted = null)
        {
            return new DoAsyncObservable<T>(@this, onNext, onErrorResume, onCompleted);
        }

        public AsyncObservable<T> Do(Action<T>? onNext = null,
                                     Action<Exception>? onErrorResume = null,
                                     Action<Result>? onCompleted = null)
        {
            return new DoSyncObservable<T>(@this, onNext, onErrorResume, onCompleted);
        }
    }

    sealed class DoAsyncObservable<T>(AsyncObservable<T> source, 
                                      Func<T, CancellationToken, ValueTask>? onNext,
                                      Func<Exception, CancellationToken, ValueTask>? onErrorResume,
                                      Func<Result, ValueTask>? onCompleted) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var doObserver = new DoAsyncObserver(observer, onNext, onErrorResume, onCompleted);
            return await source.SubscribeAsync(doObserver, cancellationToken);
        }

        sealed class DoAsyncObserver(AsyncObserver<T> observer,
                                      Func<T, CancellationToken, ValueTask>? onNext,
                                      Func<Exception, CancellationToken, ValueTask>? onErrorResume,
                                      Func<Result, ValueTask>? onCompleted) : AsyncObserver<T>
        {
            protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                if (onNext is not null)
                {
                    await onNext(value, cancellationToken);
                }
                await observer.OnNextAsync(value, cancellationToken);
            }

            protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                if (onErrorResume is not null)
                {
                    await onErrorResume(error, cancellationToken);
                }
                await observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override async ValueTask OnCompletedAsyncCore(Result result)
            {
                if (onCompleted is not null)
                {
                    await onCompleted(result);
                }
                await observer.OnCompletedAsync(result);
            }
        }
    }

    sealed class DoSyncObservable<T>(AsyncObservable<T> source,
                                     Action<T>? onNext,
                                     Action<Exception>? onErrorResume,
                                     Action<Result>? onCompleted) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var doObserver = new DoSyncObserver(observer, onNext, onErrorResume, onCompleted);
            return await source.SubscribeAsync(doObserver, cancellationToken);
        }

        sealed class DoSyncObserver(AsyncObserver<T> observer,
                                    Action<T>? onNext,
                                    Action<Exception>? onErrorResume,
                                    Action<Result>? onCompleted) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                onNext?.Invoke(value);
                return observer.OnNextAsync(value, cancellationToken);
            }

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                onErrorResume?.Invoke(error);
                return observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
            {
                onCompleted?.Invoke(result);
                return observer.OnCompletedAsync(result);
            }
        }
    }
}
