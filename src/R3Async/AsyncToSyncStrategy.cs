using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

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

internal sealed class SubscribedDisposable(IAsyncDisposable subscription, AsyncToSyncStrategy disposeStrategy) : IDisposable
{
    int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        disposeStrategy.Execute(DisposeCoreAsync());
    }

    ValueTask DisposeCoreAsync()
    {
        try
        {
            var a = subscription.DisposeAsync();
            return a;
        }
        catch (Exception e)
        {
            return new(Task.FromException(e));
        }
    }
}

internal sealed class PendingSubscriptionDisposable(ValueTask<IAsyncDisposable> subscriptionTask, CancellationTokenSource cts, AsyncToSyncStrategy disposeStrategy) : IDisposable
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
