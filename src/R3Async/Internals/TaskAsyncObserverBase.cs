using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals;

internal abstract class TaskAsyncObserverBase<T, TTaskValue>(CancellationToken cancellationToken) : AsyncObserver<T>
{
    readonly TaskCompletionSource<TTaskValue> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationToken _cancellationToken = cancellationToken;

    public async ValueTask<TTaskValue> WaitValueAsync()
    {
        try
        {
            using var _ = _cancellationToken.Register(static x =>
            {
                var @this = (TaskAsyncObserverBase<T, TTaskValue>)x!;
                @this._tcs.TrySetException(new OperationCanceledException(@this._cancellationToken));
            }, this);

            return await _tcs.Task;
        }
        finally
        {
            await DisposeAsync();
        }
    }

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
}
