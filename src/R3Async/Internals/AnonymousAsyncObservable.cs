using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals;

internal sealed class AnonymousAsyncObservable<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask<IAsyncDisposable>> subscribeAsync) : AsyncObservable<T>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        return subscribeAsync(observer.Wrap(), cancellationToken);
    }
}

internal sealed class AnonymousAsyncObserver<T>(Func<T, CancellationToken, ValueTask> onNextAsync,
                                                Func<Exception, CancellationToken, ValueTask>? onErrorResumeAsync = null,
                                                Func<Result, CancellationToken, ValueTask>? onCompletedAsync = null) : AsyncObserver<T>
{
    protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
    {
        return onNextAsync(value, cancellationToken);
    }

    protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
    {
        if (onErrorResumeAsync is null)
        {
            UnhandledExceptionHandler.OnUnhandledException(error);
            return default;
        }

        return onErrorResumeAsync.Invoke(error, cancellationToken);
    }

    protected override ValueTask OnCompletedAsyncCore(Result result, CancellationToken cancellationToken)
    {
        if (onCompletedAsync is null)
        {
            var exception = result.Exception;
            if (exception is not null)
            {
                UnhandledExceptionHandler.OnUnhandledException(exception);
            }

            return default;
        }
        
        return onCompletedAsync.Invoke(result, cancellationToken);
    }
}