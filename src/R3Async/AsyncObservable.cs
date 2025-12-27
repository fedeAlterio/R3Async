using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public abstract class AsyncObservable<T>
{
    public async ValueTask<IAsyncDisposable> SubscribeAsync(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await SubscribeAsyncCore(observer, cancellationToken);
            await observer.SourceSubscription.SetDisposableAsync(subscription);
            return observer;
        }
        catch
        {
            await observer.DisposeAsync();
            throw;
        }
    }

    protected abstract ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken);
}

public abstract class AsyncObserver<T> : IAsyncDisposable
{
    internal readonly SingleAssignmentAsyncDisposable SourceSubscription = new();
    readonly AsyncLocal<int> _onSomethingReentranceCount = new();
    readonly CancellationTokenSource _disposeCts = new();
    int _onSomethingCallsCount;
    TaskCompletionSource<object?>? _onSomethingCompletedTcs;
    bool _disposed;
    object Gate => _onSomethingReentranceCount;

    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        if (!ShouldEnterOnSomethingCall(cancellationToken, out var linkedCts))
            return;

        var linkedToken = linkedCts.Token;
        try
        {
            await OnNextAsyncCore(value, linkedToken);
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception e)
        {
            await OnErrorResumeAsync_Private(e, linkedToken);
        }
        finally
        {
            linkedCts.Dispose();
            ExitOnSomethingCall();
        }
    }
    protected abstract ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken);


    bool ShouldEnterOnSomethingCall(CancellationToken cancellationToken, [NotNullWhen(true)] out CancellationTokenSource? linkedCts)
    {
        lock (Gate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                linkedCts = null;
                return false;
            }
            _onSomethingReentranceCount.Value++;
            _onSomethingCallsCount++;

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            return true;
        }
    }

    bool ExitOnSomethingCall()
    {
        lock (Gate)
        {
            var count = --_onSomethingCallsCount;
            _onSomethingReentranceCount.Value--;

            if (count == 0)
            {
                _onSomethingCompletedTcs?.SetResult(null);
                return false;
            }
        }

        return true;
    }

    public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        if (!ShouldEnterOnSomethingCall(cancellationToken, out var linkedCts))
            return;

        try
        {
            await OnErrorResumeAsync_Private(error, linkedCts.Token);
        }
        finally
        {
            linkedCts.Dispose();
            ExitOnSomethingCall();
        }
    }
    protected abstract ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken);


    async ValueTask OnErrorResumeAsync_Private(Exception error, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                UnhandledExceptionHandler.OnUnhandledException(error);
                return;
            }

            await OnErrorResumeAsyncCore(error, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            UnhandledExceptionHandler.OnUnhandledException(error);
        }
        catch (Exception e)
        {
            UnhandledExceptionHandler.OnUnhandledException(e);
        }
    }


    public async ValueTask OnCompletedAsync(Result result, CancellationToken cancellationToken)
    {
        if (!ShouldEnterOnSomethingCall(cancellationToken, out var linkedCts))
            return;

        try
        {
            await OnCompletedAsyncCore(result, linkedCts.Token);
        }
        catch (Exception e)
        {
            UnhandledExceptionHandler.OnUnhandledException(e);
        }
        finally
        {
            linkedCts.Dispose();
            if (ExitOnSomethingCall()) // true if not disposed yet
            {
                await DisposeAsync();
            }

        }
    }

    protected abstract ValueTask OnCompletedAsyncCore(Result result, CancellationToken cancellationToken);


    public async ValueTask DisposeAsync()
    {
        Task? allOnSomethingCallsCompleted = null;
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;

            _disposeCts.Cancel();
            if (_onSomethingReentranceCount.Value == 0 && _onSomethingCallsCount > 0)
            {
                _onSomethingCompletedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                allOnSomethingCallsCompleted = _onSomethingCompletedTcs.Task;
            }
        }
        
        if (allOnSomethingCallsCompleted is not null)
        {
            await allOnSomethingCallsCompleted;
        }

        _disposeCts.Dispose();
        await DisposeAsync_Private();
    }

    async ValueTask DisposeAsync_Private()
    {
        await SourceSubscription.DisposeAsync();
        await DisposeAsyncCore();
    }

    protected virtual ValueTask DisposeAsyncCore() => default;
}