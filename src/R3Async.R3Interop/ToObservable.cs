using System;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace R3Async.R3Interop;

public sealed class AsyncOperationMode
{
    enum Kind
    {
        WaitSynchronously,
        Background
    }

    readonly Kind _kind;
    readonly Action<Exception>? _exceptionHandler;

    AsyncOperationMode(Kind kind, Action<Exception>? exceptionHandler)
    {
        _kind = kind;
        _exceptionHandler = exceptionHandler;
    }

    public static AsyncOperationMode WaitSynchronously { get; } = new(Kind.WaitSynchronously, null);
    public static AsyncOperationMode Background(Action<Exception>? exceptionHandler = null) => new(Kind.Background, exceptionHandler);

    internal void Execute(Func<Task> operation)
    {
        switch (_kind)
        {
            case Kind.WaitSynchronously:
                operation().GetAwaiter().GetResult();
                break;
            case Kind.Background:
                ExecuteInBackground(operation);
                break;
        }
    }

    async void ExecuteInBackground(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception e)
        {
            if (_exceptionHandler is null)
            {
                UnhandledExceptionHandler.OnUnhandledException(e);
                return;
            }

            try
            {
                _exceptionHandler(e);
            }
            catch (Exception handlerException)
            {
                UnhandledExceptionHandler.OnUnhandledException(handlerException);
            }
        }
    }
}

public sealed class ToObservableConfiguration
{
    public static ToObservableConfiguration Default { get; } = new();

    public AsyncOperationMode SubscribeMode { get; init; } = AsyncOperationMode.WaitSynchronously;
    public AsyncOperationMode DisposeMode { get; init; } = AsyncOperationMode.WaitSynchronously;
}

public static class AsyncToR3ObservableExtensions
{
    public static Observable<T> ToObservable<T>(this AsyncObservable<T> @this, ToObservableConfiguration? configuration = null)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));

        return new AsyncToObservableAdapter<T>(@this, configuration ?? ToObservableConfiguration.Default);
    }

    sealed class AsyncToObservableAdapter<T>(AsyncObservable<T> source, ToObservableConfiguration configuration) : Observable<T>
    {
        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            var cts = new CancellationTokenSource();
            var subscriptionTask = source.SubscribeAsync(new ObserverAdapter<T>(observer), cts.Token).AsTask();

            configuration.SubscribeMode.Execute(() => subscriptionTask);

            return new SubscriptionDisposable(subscriptionTask, cts, configuration.DisposeMode);
        }
    }

    sealed class ObserverAdapter<T>(Observer<T> observer) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            observer.OnNext(value);
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            observer.OnErrorResume(error);
            return default;
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            observer.OnCompleted(result.IsSuccess ? R3.Result.Success : R3.Result.Failure(result.Exception));
            return default;
        }
    }

    sealed class SubscriptionDisposable(Task<IAsyncDisposable> subscriptionTask, CancellationTokenSource cts, AsyncOperationMode disposeMode) : IDisposable
    {
        int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            cts.Cancel();
            disposeMode.Execute(DisposeCoreAsync);
        }

        async Task DisposeCoreAsync()
        {
            try
            {
                var subscription = await subscriptionTask;
                await subscription.DisposeAsync();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
