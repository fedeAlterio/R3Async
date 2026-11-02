using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

/// <summary>
/// Configures how <c>Merge</c> forwards the cancellation of a notification to the downstream observer.
/// </summary>
public sealed record MergeOptions
{
    /// <summary>
    /// When <see langword="true"/>, values are forwarded with the merge's lifetime token only, ignoring the token
    /// passed by inner observables. Avoids linking a token per value. Default: <see langword="false"/>.
    /// </summary>
    public bool DetachInnerCancellation { get; init; }

    /// <summary>The default options: <see cref="DetachInnerCancellation"/> is <see langword="false"/>.</summary>
    public static MergeOptions Default { get; } = new();
}

public static partial class AsyncObservable
{
    /// <summary>
    /// Merges an observable of observables into a single stream: subscribes to the outer observable, and as each
    /// inner observable is emitted, subscribes to it concurrently, forwarding all inner values as they arrive.
    /// The merged stream completes once the outer observable and all inner observables have completed.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by the inner observables.</typeparam>
    /// <param name="this">The observable of observables to merge.</param>
    /// <param name="options">Controls how cancellation is forwarded downstream. Defaults to <see cref="MergeOptions.Default"/>.</param>
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<AsyncObservable<T>> @this, MergeOptions? options = null) => new MergeObservableObservables<T>(@this, options ?? MergeOptions.Default);

    /// <summary>
    /// Merges an observable of observables into a single stream, subscribing to at most <paramref name="maxConcurrent"/>
    /// inner observables at a time. Additional inner observables wait for a slot to free up before being subscribed.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by the inner observables.</typeparam>
    /// <param name="this">The observable of observables to merge.</param>
    /// <param name="maxConcurrent">The maximum number of inner observables subscribed to concurrently.</param>
    /// <param name="options">Controls how cancellation is forwarded downstream. Defaults to <see cref="MergeOptions.Default"/>.</param>
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<AsyncObservable<T>> @this, int maxConcurrent, MergeOptions? options = null) => new MergeObservableObservablesWithMaxConcurrency<T>(@this, maxConcurrent, options ?? MergeOptions.Default);

    /// <summary>
    /// Subscribes concurrently to all observables in <paramref name="this"/> and merges their values into a single
    /// stream. The merged stream completes once every source observable has completed.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by the source observables.</typeparam>
    /// <param name="this">The observables to merge.</param>
    /// <param name="options">Controls how cancellation is forwarded downstream. Defaults to <see cref="MergeOptions.Default"/>.</param>
    public static AsyncObservable<T> Merge<T>(this IEnumerable<AsyncObservable<T>> @this, MergeOptions? options = null) => new MergeEnumerableObservable<T>(@this, options ?? MergeOptions.Default);

    /// <summary>Merges <paramref name="this"/> and <paramref name="other"/>, subscribing to both concurrently and forwarding values from either as they arrive.</summary>
    /// <typeparam name="T">The type of the values emitted by the source observables.</typeparam>
    /// <param name="this">The first observable to merge.</param>
    /// <param name="other">The second observable to merge.</param>
    /// <param name="options">Controls how cancellation is forwarded downstream. Defaults to <see cref="MergeOptions.Default"/>.</param>
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<T> @this, AsyncObservable<T> other, MergeOptions? options = null) => new MergeEnumerableObservable<T>([@this, other], options ?? MergeOptions.Default);

    sealed class MergeObservableObservables<T>(AsyncObservable<AsyncObservable<T>> sources, MergeOptions options) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeSubscription<T>(observer, options);
            try
            {
                await subscription.SubscribeAsync(sources, cancellationToken);
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }

            return subscription;
        }
    }

    sealed class MergeObservableObservablesWithMaxConcurrency<T>(AsyncObservable<AsyncObservable<T>> sources, int maxConcurrent, MergeOptions options) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeSubscriptionWithMaxConcurrency<T>(observer, maxConcurrent, options);
            try
            {
                await subscription.SubscribeAsync(sources, cancellationToken);
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }

            return subscription;
        }
    }

    class MergeSubscription<T> : IAsyncDisposable
    {
        int _innerActiveCount;
        bool _outerCompleted;
        readonly CancellationTokenSource _disposeCts = new();
        readonly SingleAssignmentAsyncDisposable _outerDisposable = new();
        protected readonly CancellationToken DisposedCancellationToken;
        readonly CompositeAsyncDisposable _innerDisposables = new();
        readonly AsyncGate _onSomethingGate = new();
        bool _disposed;
        readonly AsyncObserver<T> _observer;
        readonly bool _detachInnerCancellation;

        public MergeSubscription(AsyncObserver<T> observer, MergeOptions options)
        {
            _observer = observer;
            _detachInnerCancellation = options.DetachInnerCancellation;
            DisposedCancellationToken = _disposeCts.Token;
        }

        LinkedTokenScope ForwardScope(CancellationToken cancellationToken) => _detachInnerCancellation
            ? LinkedTokenScope.Borrow(DisposedCancellationToken)
            : LinkedTokenScope.Create(cancellationToken, DisposedCancellationToken);

        public async ValueTask SubscribeAsync(AsyncObservable<AsyncObservable<T>> @this, CancellationToken cancellationToken)
        {
            using var scope = LinkedTokenScope.Create(cancellationToken, DisposedCancellationToken);

            var outerSubscription = await @this.SubscribeAsync((x, _) => SubscribeInnerAsync(x), ForwardOnErrorResume, result =>
            {
                bool shouldComplete;
                lock (_disposeCts)
                {
                    _outerCompleted = true;
                    shouldComplete = _innerActiveCount == 0 || result.IsFailure;
                }

                return shouldComplete ? CompleteAsync(result) : default;
            }, scope.Token);

            await _outerDisposable.SetDisposableAsync(outerSubscription);
        }

        protected virtual async ValueTask SubscribeInnerAsync(AsyncObservable<T> inner)
        {
            try
            {
                var innerObserver = CreateInnerObserver();
                await innerObserver.SubscribeAsync(inner);
            }
            catch (Exception e)
            {
                await CompleteAsync(Result.Failure(e));
            }
        }

        protected virtual InnerAsyncObserver CreateInnerObserver() => new(this);

#if NET6_0_OR_GREATER
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
        async ValueTask ForwardOnNext(T value, CancellationToken cancellationToken)
        {
            if (_disposed) return;
            using var scope = ForwardScope(cancellationToken);
            using (await _onSomethingGate.LockAsync(DisposedCancellationToken))
            {
                if (_disposed) return;
                await _observer.OnNextAsync(value, scope.Token);
            }
        }

        async ValueTask ForwardOnErrorResume(Exception exception, CancellationToken cancellationToken)
        {
            using var scope = ForwardScope(cancellationToken);
            using (await _onSomethingGate.LockAsync(DisposedCancellationToken))
            {
                if (_disposed) return;
                await _observer.OnErrorResumeAsync(exception, scope.Token);
            }
        }

        protected async ValueTask CompleteAsync(Result? result)
        {
            lock (_disposeCts)
            {
                if (_disposed)
                {
                    if (result?.Exception is not null and var exception)
                        UnhandledExceptionHandler.OnUnhandledException(exception);
                    return;
                }

                _disposed = true;
            }

            _disposeCts.Cancel();
            await _innerDisposables.DisposeAsync();
            await _outerDisposable.DisposeAsync();
            if (result is not null)
            {
                await _observer.OnCompletedAsync(result.Value);
            }
            _disposeCts.Dispose();
        }

        public ValueTask DisposeAsync() => CompleteAsync(null);

        protected class InnerAsyncObserver(MergeSubscription<T> parent) : AsyncObserver<T>
        {
            public async ValueTask SubscribeAsync(AsyncObservable<T> inner)
            {
                lock (parent._disposeCts)
                {
                    parent._innerActiveCount++;
                }
                await parent._innerDisposables.AddAsync(this);
                await inner.SubscribeAsync(this, parent.DisposedCancellationToken);
            }

            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken) => parent.ForwardOnNext(value, cancellationToken);
            
            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken) => parent.ForwardOnErrorResume(error, cancellationToken);
            
            protected override ValueTask OnCompletedAsyncCore(Result result)
            {
                bool shouldComplete;
                lock (parent._disposeCts)
                {
                    var count = --parent._innerActiveCount;
                    shouldComplete = result.IsFailure || (count == 0 && parent._outerCompleted);
                }

                return shouldComplete ? parent.CompleteAsync(result) : default;
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                await OnDisposeAsync();
                await parent._innerDisposables.Remove(this);
            }

            protected virtual ValueTask OnDisposeAsync() => default;
        }
    }

    sealed class MergeSubscriptionWithMaxConcurrency<T>(AsyncObserver<T> observer, int maxConcurrent, MergeOptions options) : MergeSubscription<T>(observer, options)
    {
        readonly SemaphoreSlim _semaphore = new(maxConcurrent, maxConcurrent);

        protected override async ValueTask SubscribeInnerAsync(AsyncObservable<T> inner)
        {
            await _semaphore.WaitAsync(DisposedCancellationToken);
            InnerAsyncObserverWithSemaphore? innerObserver = null;
            try
            {
                innerObserver = new InnerAsyncObserverWithSemaphore(this);
                await innerObserver.SubscribeAsync(inner);
            }
            catch (Exception e)
            {
                // A failed inner observer has normally already been disposed, releasing our slot
                // through OnDisposeAsync; ReleaseSemaphoreOnce guarantees exactly one release per
                // acquired slot no matter which side got there first.
                if (innerObserver is null)
                    _semaphore.Release();
                else
                    innerObserver.ReleaseSemaphoreOnce();

                await CompleteAsync(Result.Failure(e));
            }
        }

        sealed class InnerAsyncObserverWithSemaphore(MergeSubscriptionWithMaxConcurrency<T> parent) : InnerAsyncObserver(parent)
        {
            int _semaphoreReleased;

            public void ReleaseSemaphoreOnce()
            {
                if (Interlocked.Exchange(ref _semaphoreReleased, 1) == 0)
                {
                    parent._semaphore.Release();
                }
            }

            protected override ValueTask OnDisposeAsync()
            {
                ReleaseSemaphoreOnce();
                return default;
            }
        }
    }

    sealed class MergeEnumerableObservable<T>(IEnumerable<AsyncObservable<T>> sources, MergeOptions options) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeEnumerableSubscription(observer, sources, options);
            await subscription.StartAsync();
            return subscription;
        }

        sealed class MergeEnumerableSubscription : IAsyncDisposable
        {
            readonly IEnumerable<AsyncObservable<T>> _sources;
            readonly CompositeAsyncDisposable _innerDisposables = new();
            readonly CancellationTokenSource _cts = new();
            readonly CancellationToken _disposedCancellationToken;
            readonly AsyncGate _onSomethingGate = new();
            readonly object _stateGate = new();
            readonly TaskCompletionSource<bool> _subscriptionFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int _active;
            bool _enumerationCompleted;
            bool _disposed;
            readonly AsyncObserver<T> _observer;
            readonly bool _detachInnerCancellation;

            public MergeEnumerableSubscription(AsyncObserver<T> observer, IEnumerable<AsyncObservable<T>> sources, MergeOptions options)
            {
                _observer = observer;
                _sources = sources;
                _detachInnerCancellation = options.DetachInnerCancellation;
                _disposedCancellationToken = _cts.Token;
            }

            LinkedTokenScope ForwardScope(CancellationToken token) => _detachInnerCancellation
                ? LinkedTokenScope.Borrow(_disposedCancellationToken)
                : LinkedTokenScope.Create(token, _disposedCancellationToken);

            public async ValueTask StartAsync()
            {
                try
                {
                    ExecutionFlow.Enter(this);
                    try
                    {
                        foreach (var src in _sources)
                        {
                            if (_disposedCancellationToken.IsCancellationRequested)
                                return;
                            lock (_stateGate)
                            {
                                _active++;
                            }

                            var innerObserver = new InnerAsyncObserver(this);
                            await _innerDisposables.AddAsync(innerObserver);
                            try
                            {
                                await src.SubscribeAsync(innerObserver, _disposedCancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                            catch (Exception ex)
                            {
                                await CompleteAsync(Result.Failure(ex));
                                return;
                            }
                        }

                        bool shouldComplete;
                        lock (_stateGate)
                        {
                            _enumerationCompleted = true;
                            shouldComplete = _active == 0;
                        }

                        if (shouldComplete)
                        {
                            await CompleteAsync(Result.Success);
                        }
                    }
                    catch (Exception e)
                    {
                        await CompleteAsync(Result.Failure(e));
                    }
                    finally
                    {
                        _subscriptionFinished.SetResult(true);
                    }
                }
                catch (Exception e)
                {
                    UnhandledExceptionHandler.OnUnhandledException(e);
                }
            }

#if NET6_0_OR_GREATER
            [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
            async ValueTask OnNextAsync(T value, CancellationToken token)
            {
                using var scope = ForwardScope(token);
                using (await _onSomethingGate.LockAsync(_disposedCancellationToken))
                {
                    lock (_stateGate)
                    {
                        if (_disposed) return;
                    }

                    await _observer.OnNextAsync(value, scope.Token);
                }
            }

            async ValueTask OnErrorResumeAsync(Exception ex, CancellationToken token)
            {
                using var scope = ForwardScope(token);
                using (await _onSomethingGate.LockAsync(_disposedCancellationToken))
                {
                    lock (_stateGate)
                    {
                        if (_disposed) return;
                    }

                    await _observer.OnErrorResumeAsync(ex, scope.Token);
                }
            }

            ValueTask OnCompletedAsync(Result result)
            {
                if (result.IsFailure)
                {
                    return CompleteAsync(result);
                }

                bool shouldComplete;
                lock (_stateGate)
                {
                    _active--;
                    shouldComplete = _active == 0 && _enumerationCompleted;
                }

                return shouldComplete ? CompleteAsync(Result.Success) : default;
            }

            async ValueTask CompleteAsync(Result? result)
            {
                lock (_stateGate)
                {
                    if (_disposed)
                    {
                        if (result?.Exception is not null and var ex)
                            UnhandledExceptionHandler.OnUnhandledException(ex);
                        return;
                    }

                    _disposed = true;
                }

                _cts.Cancel();
                await _innerDisposables.DisposeAsync();
                if (!ExecutionFlow.Contains(this))
                {
                    await _subscriptionFinished.Task;
                }

                if (result is not null)
                {
                    await _observer.OnCompletedAsync(result.Value);
                }
                _cts.Dispose();
            }

            public ValueTask DisposeAsync() => CompleteAsync(null);
            sealed class InnerAsyncObserver(MergeEnumerableSubscription parent) : AsyncObserver<T>
            {
                protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                    => parent.OnNextAsync(value, cancellationToken);

                protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                    => parent.OnErrorResumeAsync(error, cancellationToken);

                protected override ValueTask OnCompletedAsyncCore(Result result)
                    => parent.OnCompletedAsync(result);

                protected override async ValueTask DisposeAsyncCore()
                {
                    await parent._innerDisposables.Remove(this);
                }
            }
        }
    }
}