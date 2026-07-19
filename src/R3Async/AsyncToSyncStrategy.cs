using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// Governs how a single async operation (e.g. subscribing or disposing) is consumed from synchronous code, such as
/// when adapting an <see cref="AsyncObservable{T}"/> to a synchronous <see cref="IObservable{T}"/> or R3 <c>Observable&lt;T&gt;</c>.
/// </summary>
public sealed class AsyncToSyncStrategy
{
    readonly Action<Exception>? _onException;

    private AsyncToSyncStrategy(Action<Exception>? onException) => _onException = onException;

    static readonly AsyncToSyncStrategy DefaultFireAndForget = new(null);

    /// <summary>
    /// A strategy that blocks the calling thread until the async operation completes. Exceptions thrown by the
    /// operation propagate synchronously to the caller.
    /// </summary>
    public static AsyncToSyncStrategy Blocking { get; } = new(null);

    /// <summary>
    /// Creates a strategy that starts the async operation without waiting for it to complete. Exceptions thrown by the
    /// operation are routed to <paramref name="onException"/> if provided, otherwise to the <see cref="UnhandledExceptionHandler"/>.
    /// </summary>
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

/// <summary>
/// Configures how the async subscribe and dispose operations of an <see cref="AsyncObservable{T}"/> are consumed when
/// adapting it to a synchronous observable (<see cref="IObservable{T}"/> or R3's <c>Observable&lt;T&gt;</c>).
/// </summary>
public sealed class ToObservableConfiguration
{
    /// <summary>The strategy used to consume the async <c>SubscribeAsync</c> operation from synchronous <c>Subscribe</c> calls.</summary>
    public required AsyncToSyncStrategy SubscribeStrategy { get; init; }

    /// <summary>The strategy used to consume the async <c>DisposeAsync</c> operation from synchronous <c>Dispose</c> calls.</summary>
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
