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
    readonly AsyncLocal<bool> _reentrantCallPending = new();
    readonly CancellationTokenSource _disposeCts = new();
    bool _callPending;
    TaskCompletionSource<object?>? _allCallsCompletedTcs;
    
    IAsyncDisposable? _sourceSubscription;
    protected bool Subscribed => _sourceSubscription is not null;
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

    bool TryEnterOnSomethingCall(CancellationToken cancellationToken, [NotNullWhen(true)] out CancellationTokenSource? linkedCts)
    {
        lock (_reentrantCallPending)
        {
            if (_disposeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                linkedCts = null;
                return false;
            }

            if (_callPending)
            {
                throw new InvalidOperationException($"Concurrent calls of {nameof(OnNextAsync)}, {nameof(OnErrorResumeAsync)}, {nameof(OnCompletedAsync)} are not allowed. There is already a call pending");
            }
            Debug.Assert(!_reentrantCallPending.Value);

            _callPending = true;
            _reentrantCallPending.Value = true;

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            return true;
        }
    }

    bool ExitOnSomethingCall()
    {
        lock (_reentrantCallPending)
        {
            Debug.Assert(_callPending);
            Debug.Assert(_reentrantCallPending.Value);
            _callPending = false;
            _reentrantCallPending.Value = false;
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


    public async ValueTask DisposeAsync()
    {
        Task? allOnSomethingCallsCompleted = null;
        lock (_reentrantCallPending)
        {
            if (_disposeCts.IsCancellationRequested) return;

            _disposeCts.Cancel();
            if (!_reentrantCallPending.Value && _callPending)
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
        await SingleAssignmentAsyncDisposable.DisposeAsync(ref _sourceSubscription);
        await DisposeAsyncCore();
    }

    protected virtual ValueTask DisposeAsyncCore() => default;
}