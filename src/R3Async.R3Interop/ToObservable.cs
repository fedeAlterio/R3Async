using System;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace R3Async.R3Interop;

public sealed class AsyncToSyncStrategy
{
    readonly Action<Exception>? _onException;

    private AsyncToSyncStrategy(Action<Exception>? onException) => _onException = onException;

    static readonly AsyncToSyncStrategy DefaultFireAndForget = new(null);
    public static AsyncToSyncStrategy Blocking { get; } = new(null);

    public static AsyncToSyncStrategy FireAndForget(Action<Exception>? onException = null) =>
        onException is null ? DefaultFireAndForget : new(onException);

    internal bool IsBlocking => ReferenceEquals(this, Blocking);

    internal void Execute(ValueTask operation)
    {
        if (IsBlocking)
        {
            if (operation.IsCompleted)
                operation.GetAwaiter().GetResult();
            else
                operation.AsTask().GetAwaiter().GetResult();

            return;
        }

        ExecuteFireAndForget(operation);
    }

    async void ExecuteFireAndForget(ValueTask operation)
    {
        try
        {
            await operation;
        }
        catch (Exception e)
        {
            if (_onException is null)
            {
                UnhandledExceptionHandler.OnUnhandledException(e);
                return;
            }

            try
            {
                _onException(e);
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
    public required AsyncToSyncStrategy SubscribeStrategy { get; init; } 
    public required AsyncToSyncStrategy DisposeStrategy { get; init; }
}

public static class AsyncToR3ObservableExtensions
{
    public static Observable<T> ToObservable<T>(this AsyncObservable<T> @this, ToObservableConfiguration configuration)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));

        return new AsyncToObservableAdapter<T>(@this, configuration);
    }

    sealed class AsyncToObservableAdapter<T>(AsyncObservable<T> source, ToObservableConfiguration configuration) : Observable<T>
    {
        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            return configuration.SubscribeStrategy.IsBlocking
                ? SubscribeBlocking(observer)
                : SubscribeFireAndForget(observer);
        }

        IDisposable SubscribeBlocking(Observer<T> observer)
        {
            var subscriptionTask = source.SubscribeAsync(new ObserverAdapter<T>(observer), CancellationToken.None);
            var subscription = subscriptionTask.IsCompletedSuccessfully
                ? subscriptionTask.GetAwaiter().GetResult()
                : subscriptionTask.AsTask().GetAwaiter().GetResult();

            return new SubscribedDisposable(subscription, configuration.DisposeStrategy);
        }

        IDisposable SubscribeFireAndForget(Observer<T> observer)
        {
            var cts = new CancellationTokenSource();

            ValueTask<IAsyncDisposable> subscriptionTask;
            try
            {
                subscriptionTask = source.SubscribeAsync(new ObserverAdapter<T>(observer), cts.Token).Preserve();
            }
            catch (Exception e)
            {
                subscriptionTask = new(Task.FromException<IAsyncDisposable>(e));
            }

            configuration.SubscribeStrategy.Execute(AwaitSubscription(subscriptionTask));

            return new PendingSubscriptionDisposable(subscriptionTask, cts, configuration.DisposeStrategy);
        }

        static async ValueTask AwaitSubscription(ValueTask<IAsyncDisposable> subscriptionTask) => await subscriptionTask;
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

    sealed class SubscribedDisposable(IAsyncDisposable subscription, AsyncToSyncStrategy disposeStrategy) : IDisposable
    {
        int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            disposeStrategy.Execute(DisposeCoreAsync());
        }

        ValueTask DisposeCoreAsync() => subscription.DisposeAsync();
    }

    sealed class PendingSubscriptionDisposable(ValueTask<IAsyncDisposable> subscriptionTask, CancellationTokenSource cts, AsyncToSyncStrategy disposeStrategy) : IDisposable
    {
        int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            cts.Cancel();
            disposeStrategy.Execute(DisposeCoreAsync());
        }

        async ValueTask DisposeCoreAsync()
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
