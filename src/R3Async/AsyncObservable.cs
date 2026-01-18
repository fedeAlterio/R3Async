using System;
using System.Diagnostics;
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
            await observer.SetSourceSubscriptionAsync(subscription);
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
    readonly AsyncLocal<int> _reentrantCallsCount = new();
    readonly CancellationTokenSource _disposeCts = new();
    int _callsCount;
    TaskCompletionSource<object?>? _allCallsCompletedTcs;
    internal bool IsDisposed => _disposeCts.IsCancellationRequested;
    IAsyncDisposable? _sourceSubscription;
    internal ValueTask SetSourceSubscriptionAsync(IAsyncDisposable? value) => SingleAssignmentAsyncDisposable.SetDisposableAsync(ref _sourceSubscription, value);

    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        if (!TryEnterOnSomethingCall(cancellationToken, out var linkedCts))
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

    [DebuggerStepThrough]
    bool TryEnterOnSomethingCall(CancellationToken cancellationToken, [NotNullWhen(true)] out CancellationTokenSource? linkedCts)
    {
        lock (_reentrantCallsCount)
        {
            if (_disposeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                linkedCts = null;
                return false;
            }

            int reentrantCallsCount = _reentrantCallsCount.Value;
            if (_callsCount != reentrantCallsCount)
            {
                UnhandledExceptionHandler.OnUnhandledException(new ConcurrentObserverCallsException());
                linkedCts = null;
                return false;
            }

            _callsCount++;
            _reentrantCallsCount.Value = reentrantCallsCount + 1;

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            return true;
        }
    }

    [DebuggerStepThrough]
    bool ExitOnSomethingCall()
    {
        lock (_reentrantCallsCount)
        {
            _callsCount--;
            int reentrantCallsCount = --_reentrantCallsCount.Value;
            Debug.Assert(reentrantCallsCount >= 0);
            Debug.Assert(_callsCount == reentrantCallsCount);
            if (_allCallsCompletedTcs is not null)
            {
                _allCallsCompletedTcs.SetResult(null);
                return false;
            }
        }

        return true;
    }

    public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        if (!TryEnterOnSomethingCall(cancellationToken, out var linkedCts))
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

    [DebuggerStepThrough]
    public async ValueTask OnCompletedAsync(Result result)
    {
        if (!TryEnterOnSomethingCall(CancellationToken.None, out var linkedCts))
            return;

        try
        {
            await OnCompletedAsyncCore(result);
        }
        catch (Exception e)
        {
            UnhandledExceptionHandler.OnUnhandledException(e);
        }
        finally
        {
            linkedCts.Dispose();
            if (ExitOnSomethingCall())
            {
                await DisposeAsync();
            }
        }
    }

    protected abstract ValueTask OnCompletedAsyncCore(Result result);


    [DebuggerStepThrough]
    public async ValueTask DisposeAsync()
    {
        Task? allOnSomethingCallsCompleted = null;
        lock (_reentrantCallsCount)
        {
            if (_disposeCts.IsCancellationRequested) return;

            _disposeCts.Cancel();
            if (_reentrantCallsCount.Value == 0 && _callsCount > 0)
            {
                _allCallsCompletedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                allOnSomethingCallsCompleted = _allCallsCompletedTcs.Task;
            }
        }
        
        if (allOnSomethingCallsCompleted is not null)
        {
            await allOnSomethingCallsCompleted;
        }

        _disposeCts.Dispose();
        try
        {
            await SingleAssignmentAsyncDisposable.DisposeAsync(ref _sourceSubscription);
        }
        catch(Exception e)
        {
            UnhandledExceptionHandler.OnUnhandledException(e);
        }

        try
        {
            await DisposeAsyncCore();
        }
        catch (Exception e)
        {
            UnhandledExceptionHandler.OnUnhandledException(e);
        }
    }

    [DebuggerStepThrough]
    protected virtual ValueTask DisposeAsyncCore() => default;
}

public class ConcurrentObserverCallsException() : Exception($"Concurrent calls of {nameof(AsyncObserver<>.OnNextAsync)}, {nameof(AsyncObserver<>.OnErrorResumeAsync)}, {nameof(AsyncObserver<>.OnCompletedAsync)} are not allowed. There is already a call pending");