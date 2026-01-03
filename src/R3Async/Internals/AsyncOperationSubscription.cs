using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals;

internal static class AsyncOperationSubscription
{
    public static AsyncOperationSubscription<T> CreateAndRun<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> runAsyncCore, AsyncObserver<T> observer)
    {
        var ret = new AnonymousAsyncOperationSubscription<T>(runAsyncCore, observer);
        ret.Run();
        return ret;
    }

    class AnonymousAsyncOperationSubscription<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> runAsyncCore, AsyncObserver<T> observer) : AsyncOperationSubscription<T>(observer)
    {
        protected override ValueTask RunAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            return runAsyncCore(observer, cancellationToken);
        }
    }
}

internal abstract class AsyncOperationSubscription<T>(AsyncObserver<T> observer) : IAsyncDisposable
{
    readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _cts = new();
    readonly AsyncLocal<bool> _reentrant = new();
    public void Run() => _ = RunAsync(_cts.Token);
    async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        _reentrant.Value = true;
        try
        {
            await RunAsyncCore(observer, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            try
            {
                await observer.OnCompletedAsync(Result.Failure(e));
            }
            catch (Exception exception)
            {
                UnhandledExceptionHandler.OnUnhandledException(exception);
            }
        }
        finally
        {
            _tcs.SetResult(true);
        }
    }
    protected abstract ValueTask RunAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (!_reentrant.Value)
        {
            await _tcs.Task;
        }
        _cts.Dispose();
    }
}
