using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

/// <summary>
/// Represents an asynchronous reactive stream of values of type <typeparamref name="T"/>. This is the async
/// counterpart of R3's synchronous <c>Observable&lt;T&gt;</c>: instead of pushing values synchronously, it awaits
/// the observer's <c>OnNextAsync</c>/<c>OnErrorResumeAsync</c>/<c>OnCompletedAsync</c> callbacks, so slow consumers
/// naturally apply backpressure to the source.
/// </summary>
/// <typeparam name="T">The type of the values produced by the stream.</typeparam>
public abstract class AsyncObservable<T>
{
    /// <summary>
    /// Subscribes <paramref name="observer"/> to this observable, returning an <see cref="IAsyncDisposable"/> that
    /// unsubscribes (and disposes the observer) when disposed.
    /// </summary>
    /// <remarks>
    /// <paramref name="cancellationToken"/> only guards the subscription operation itself (e.g. connecting to the
    /// source); it does not cancel the stream once subscribed. To stop receiving notifications, dispose the
    /// returned <see cref="IAsyncDisposable"/>. If subscribing throws, the observer is disposed before the
    /// exception propagates.
    /// </remarks>
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

/// <summary>
/// Receives asynchronous notifications from an <see cref="AsyncObservable{T}"/>: values via
/// <see cref="OnNextAsync"/>, resumable errors via <see cref="OnErrorResumeAsync"/>, and a single terminal
/// <see cref="OnCompletedAsync"/>. Implements <see cref="IAsyncDisposable"/> so the subscription can be torn down
/// by disposing the observer.
/// </summary>
/// <remarks>
/// Calls to <see cref="OnNextAsync"/>, <see cref="OnErrorResumeAsync"/>, and <see cref="OnCompletedAsync"/> must
/// not be made concurrently on the same instance; a concurrent call is detected and routed to
/// <see cref="UnhandledExceptionHandler"/> as a <see cref="ConcurrentObserverCallsException"/> rather than being
/// delivered. Reentrant calls made synchronously from within an in-flight call are allowed.
/// </remarks>
/// <typeparam name="T">The type of the values received by the observer.</typeparam>
public abstract class AsyncObserver<T> : IAsyncDisposable
{
    readonly object _gate = new();
    readonly CancellationTokenSource _disposeCts = new();
    ObserverCallToken? _currentCall;
    TaskCompletionSource<object?>? _allCallsCompletedTcs;

    // Linked with _disposeCts and reused while the caller keeps passing the same token.
    CancellationToken _linkedCallerToken;
    CancellationTokenSource? _linkedCts;

    sealed class ObserverCallToken : FlowToken;
    internal bool IsDisposed => _disposeCts.IsCancellationRequested;
    IAsyncDisposable? _sourceSubscription;
    internal ValueTask SetSourceSubscriptionAsync(IAsyncDisposable? value) => SingleAssignmentAsyncDisposable.SetDisposableAsync(ref _sourceSubscription, value);

    /// <summary>
    /// Delivers the next value in the stream. If the observer is already disposed or
    /// <paramref name="cancellationToken"/> is already canceled, the call is silently dropped. Any exception
    /// thrown by the implementation is routed to <see cref="OnErrorResumeAsync"/> rather than propagating to the
    /// caller; an <see cref="OperationCanceledException"/> is swallowed instead.
    /// </summary>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        if (!TryEnterOnSomethingCall(cancellationToken, out var scope))
            return;

        var linkedToken = scope.Token;
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
            scope.Dispose();
            ExitOnSomethingCall();
        }
    }
    /// <summary>
    /// When overridden, implements the observer-specific handling of a value delivered via <see cref="OnNextAsync"/>.
    /// </summary>
    protected abstract ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken);

    [DebuggerStepThrough]
    bool TryEnterOnSomethingCall(CancellationToken cancellationToken, out LinkedTokenScope scope)
    {
        lock (_gate)
        {
            if (_disposeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                scope = default;
                return false;
            }

            var currentCall = _currentCall;
            if (currentCall is null)
            {
                var token = new ObserverCallToken();
                _currentCall = token;
                ExecutionFlow.Enter(token);
                scope = LinkedTokenScope.Borrow(LinkedCallToken(cancellationToken));
                return true;
            }

            if (ExecutionFlow.Contains(currentCall))
            {
                currentCall.Count++;
                scope = LinkedTokenScope.Create(cancellationToken, _disposeCts.Token);
                return true;
            }

            UnhandledExceptionHandler.OnUnhandledException(new ConcurrentObserverCallsException());
            scope = default;
            return false;
        }
    }

    CancellationToken LinkedCallToken(CancellationToken callerToken)
    {
        if (!callerToken.CanBeCanceled)
        {
            return _disposeCts.Token;
        }

        if (_linkedCts is null || _linkedCallerToken != callerToken)
        {
            _linkedCts?.Dispose();
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _disposeCts.Token);
            _linkedCallerToken = callerToken;
        }

        return _linkedCts.Token;
    }

    void DisposeLinkedCallToken()
    {
        _linkedCts?.Dispose();
        _linkedCts = null;
        _linkedCallerToken = default;
    }

    [DebuggerStepThrough]
    bool ExitOnSomethingCall()
    {
        lock (_gate)
        {
            var currentCall = _currentCall;
            Debug.Assert(currentCall is not null);
            Debug.Assert(currentCall.Count > 0);

            if (--currentCall.Count == 0)
            {
                _currentCall = null;
                if (_disposeCts.IsCancellationRequested)
                {
                    DisposeLinkedCallToken();
                }

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

    /// <summary>
    /// Delivers a resumable error notification: unlike <see cref="OnCompletedAsync"/>, this does not terminate the
    /// stream, allowing the source to keep emitting afterward. If handling the error itself throws or the
    /// <paramref name="cancellationToken"/> is already canceled, the original error is forwarded to
    /// <see cref="UnhandledExceptionHandler"/> instead.
    /// </summary>
    public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        if (!TryEnterOnSomethingCall(cancellationToken, out var scope))
            return;

        try
        {
            await OnErrorResumeAsync_Private(error, scope.Token);
        }
        finally
        {
            scope.Dispose();
            ExitOnSomethingCall();
        }
    }
    /// <summary>
    /// When overridden, implements the observer-specific handling of an error delivered via
    /// <see cref="OnErrorResumeAsync"/>.
    /// </summary>
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

    /// <summary>
    /// Delivers the terminal completion notification, either <see cref="Result.Success"/> or a
    /// <see cref="Result.Failure(Exception)"/>. This is the last notification the observer will receive; once it
    /// returns, the observer disposes itself (and its source subscription).
    /// </summary>
    [DebuggerStepThrough]
    public async ValueTask OnCompletedAsync(Result result)
    {
        if (!TryEnterOnSomethingCall(CancellationToken.None, out var scope))
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
            scope.Dispose();
            if (ExitOnSomethingCall())
            {
                await DisposeAsync();
            }
        }
    }

    /// <summary>
    /// When overridden, implements the observer-specific handling of the terminal notification delivered via
    /// <see cref="OnCompletedAsync"/>.
    /// </summary>
    protected abstract ValueTask OnCompletedAsyncCore(Result result);


    /// <summary>
    /// Disposes the observer: cancels any pending notification, waits for an in-flight call chain to unwind (unless
    /// called reentrantly from within that chain, to avoid a deadlock), then runs <see cref="DisposeAsyncCore"/>
    /// and disposes the upstream source subscription. Safe to call multiple times; subsequent calls are no-ops.
    /// Exceptions thrown by cleanup are routed to <see cref="UnhandledExceptionHandler"/> rather than propagating.
    /// </summary>
    [DebuggerStepThrough]
    public async ValueTask DisposeAsync()
    {
        Task? allOnSomethingCallsCompleted = null;
        lock (_gate)
        {
            if (_disposeCts.IsCancellationRequested) return;

            _disposeCts.Cancel();

            // With a call in flight, its last ExitOnSomethingCall releases the linked token instead.
            if (_currentCall is null)
            {
                DisposeLinkedCallToken();
            }
            // Wait for the in-flight call chain unless this flow is part of it (disposing from
            // within a call must not deadlock on itself).
            else if (!ExecutionFlow.Contains(_currentCall))
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

    /// <summary>
    /// When overridden, releases resources owned by this observer. Called once, after the in-flight call chain has
    /// unwound and before the source subscription is disposed. The default implementation does nothing.
    /// </summary>
    [DebuggerStepThrough]
    protected virtual ValueTask DisposeAsyncCore() => default;
}

/// <summary>
/// Thrown when <see cref="AsyncObserver{T}.OnNextAsync"/>, <see cref="AsyncObserver{T}.OnErrorResumeAsync"/>, or
/// <see cref="AsyncObserver{T}.OnCompletedAsync"/> is called concurrently with another still-in-flight call on the
/// same observer instance. This exception is routed to <see cref="UnhandledExceptionHandler"/>; the offending call
/// is dropped rather than propagating the exception to its caller, so it does not terminate the observable chain.
/// </summary>
public class ConcurrentObserverCallsException() : Exception($"Concurrent calls of {nameof(AsyncObserver<>.OnNextAsync)}, {nameof(AsyncObserver<>.OnErrorResumeAsync)}, {nameof(AsyncObserver<>.OnCompletedAsync)} are not allowed. There is already a call pending");