using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals;

internal abstract class TaskAsyncObserverBase<T, TTaskValue>(CancellationToken cancellationToken) : AsyncObserver<T>
{
    readonly TaskCompletionSource<TTaskValue> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationToken _cancellationToken = cancellationToken;

    public async ValueTask<TTaskValue> WaitValueAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = LinkedTokenScope.Create(cancellationToken, _cancellationToken);
            return await _tcs.Task.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, scope.Token);
        }
        finally
        {
            await DisposeAsync();
        }
    }

    [DebuggerStepThrough]
    protected async ValueTask TrySetCompleted(TTaskValue value)
    {
        try
        {
            _tcs.TrySetResult(value);
        }
        finally
        {
            await DisposeAsync();
        }
    }

    protected async ValueTask TrySetException(Exception e)
    {
        try
        {
            _tcs.TrySetException(e);
        }
        finally
        {
            await DisposeAsync();
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _tcs.TrySetException(new OperationCanceledException("Underlying subscription disposed"));
        return base.DisposeAsyncCore();
    }
}

internal static class TaskAsyncObserverBaseEx
{
    public static async ValueTask<SubscriptionHandle<TValue>> ToSubscriptionAsyncHandleAsync<T, TValue>(
        this AsyncObservable<T> source,
        TaskAsyncObserverBase<T, TValue> observer,
        CancellationToken cancellationToken)
    {
        var subscription = await source.SubscribeAsync(observer, cancellationToken);
        return new SubscriptionHandle<TValue>(observer.WaitValueAsync, subscription);
    }
}
