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
    // Same owner-token scheme as AsyncGate: the flow-local token is only a claim, and it counts as
    // the in-flight call chain only while it reference-equals _currentCall. A token inherited by a
    // flow forked during a call goes stale as soon as the chain unwinds, so later calls from that
    // flow are neither treated as reentrant nor misreported as concurrent.
    readonly AsyncLocal<ObserverCallToken?> _callToken = new();
    readonly CancellationTokenSource _disposeCts = new();
    ObserverCallToken? _currentCall;
    TaskCompletionSource<object?>? _allCallsCompletedTcs;

    sealed class ObserverCallToken
    {
        public int Count = 1;
    }
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
        lock (_callToken)
        {
            if (_disposeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                linkedCts = null;
                return false;
            }

            var currentCall = _currentCall;
            if (currentCall is null)
            {
                var token = new ObserverCallToken();
                _currentCall = token;
                _callToken.Value = token;
            }
            else if (ReferenceEquals(_callToken.Value, currentCall))
            {
                currentCall.Count++;
            }
            else
            {
                UnhandledExceptionHandler.OnUnhandledException(new ConcurrentObserverCallsException());
                linkedCts = null;
                return false;
            }

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            return true;
        }
    }

    [DebuggerStepThrough]
    bool ExitOnSomethingCall()
    {
        lock (_callToken)
        {
            var currentCall = _currentCall;
            Debug.Assert(currentCall is not null);
            Debug.Assert(currentCall.Count > 0);

            if (--currentCall.Count == 0)
            {
                _currentCall = null;
                if (_allCallsCompletedTcs is not null)
                {
                    _allCallsCompletedTcs.SetResult(null);
                    return false;
                }
            }
            else if (_allCallsCompletedTcs is not null)
            {
                // A disposer is waiting for the chain to unwind; the last exit will signal it.
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
        lock (_callToken)
        {
            if (_disposeCts.IsCancellationRequested) return;

            _disposeCts.Cancel();

            // Wait for the in-flight call chain unless this flow is part of it (disposing from
            // within a call must not deadlock on itself).
            if (_currentCall is not null && !ReferenceEquals(_callToken.Value, _currentCall))
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
            await DisposeAsyncCore();
        }
        catch (Exception e)
        {
            UnhandledExceptionHandler.OnUnhandledException(e);
        }

        try
        {
            await SingleAssignmentAsyncDisposable.DisposeAsync(ref _sourceSubscription);
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