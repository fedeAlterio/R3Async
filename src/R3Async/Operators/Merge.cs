using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Merges an observable of observables into a single stream: subscribes to the outer observable, and as each
    /// inner observable is emitted, subscribes to it concurrently, forwarding all inner values as they arrive.
    /// The merged stream completes once the outer observable and all inner observables have completed.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by the inner observables.</typeparam>
    /// <param name="this">The observable of observables to merge.</param>
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<AsyncObservable<T>> @this) => new MergeObservableObservables<T>(@this);

    /// <summary>
    /// Merges an observable of observables into a single stream, subscribing to at most <paramref name="maxConcurrent"/>
    /// inner observables at a time. Additional inner observables wait for a slot to free up before being subscribed.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by the inner observables.</typeparam>
    /// <param name="this">The observable of observables to merge.</param>
    /// <param name="maxConcurrent">The maximum number of inner observables subscribed to concurrently.</param>
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<AsyncObservable<T>> @this, int maxConcurrent) => new MergeObservableObservablesWithMaxConcurrency<T>(@this, maxConcurrent);

    /// <summary>
    /// Subscribes concurrently to all observables in <paramref name="this"/> and merges their values into a single
    /// stream. The merged stream completes once every source observable has completed.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by the source observables.</typeparam>
    /// <param name="this">The observables to merge.</param>
    public static AsyncObservable<T> Merge<T>(this IEnumerable<AsyncObservable<T>> @this) => new MergeEnumerableObservable<T>(@this);

    /// <summary>Merges <paramref name="this"/> and <paramref name="other"/>, subscribing to both concurrently and forwarding values from either as they arrive.</summary>
    /// <typeparam name="T">The type of the values emitted by the source observables.</typeparam>
    /// <param name="this">The first observable to merge.</param>
    /// <param name="other">The second observable to merge.</param>
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<T> @this, AsyncObservable<T> other) => new MergeEnumerableObservable<T>([@this, other]);

    sealed class MergeObservableObservables<T>(AsyncObservable<AsyncObservable<T>> sources) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeSubscription<T>(observer);
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

    sealed class MergeObservableObservablesWithMaxConcurrency<T>(AsyncObservable<AsyncObservable<T>> sources, int maxConcurrent) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeSubscriptionWithMaxConcurrency<T>(observer, maxConcurrent);
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

        public MergeSubscription(AsyncObserver<T> observer)
        {
            _observer = observer;
            DisposedCancellationToken = _disposeCts.Token;
        }

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

        async ValueTask ForwardOnNext(T value, CancellationToken cancellationToken)
        {
            if (_disposed) return;
            using var scope = LinkedTokenScope.Create(cancellationToken, DisposedCancellationToken);
            using (await _onSomethingGate.LockAsync())
            {
                if (_disposed) return;
                await _observer.OnNextAsync(value, scope.Token);
            }
        }

        async ValueTask ForwardOnErrorResume(Exception exception, CancellationToken cancellationToken)
        {
            using var scope = LinkedTokenScope.Create(cancellationToken, DisposedCancellationToken);
            using (await _onSomethingGate.LockAsync())
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

    sealed class MergeSubscriptionWithMaxConcurrency<T>(AsyncObserver<T> observer, int maxConcurrent) : MergeSubscription<T>(observer)
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

        protected override InnerAsyncObserver CreateInnerObserver() => new InnerAsyncObserverWithSemaphore(this);

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

    sealed class MergeEnumerableObservable<T>(IEnumerable<AsyncObservable<T>> sources) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeEnumerableSubscription(observer, sources);
            try
            {
                subscription.StartAsync();
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }

            return subscription;
        }

        sealed class MergeEnumerableSubscription : IAsyncDisposable
        {
            readonly IEnumerable<AsyncObservable<T>> _sources;
            readonly CompositeAsyncDisposable _innerDisposables = new();
            readonly CancellationTokenSource _cts = new();
            readonly CancellationToken _disposedCancellationToken;
            readonly AsyncGate _onSomethingGate = new();
            readonly TaskCompletionSource<bool> _subscriptionFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly AsyncLocal<bool> _reentrant = new();
            int _active;
            bool _enumerationCompleted;
            bool _disposed;
            readonly AsyncObserver<T> _observer;

            public MergeEnumerableSubscription(AsyncObserver<T> observer, IEnumerable<AsyncObservable<T>> sources)
            {
                _observer = observer;
                _sources = sources;
                _disposedCancellationToken = _cts.Token;
            }

            public async void StartAsync()
            {
                try
                {
                    _reentrant.Value = true;
                    try
                    {
                        foreach (var src in _sources)
                        {
                            if (_disposedCancellationToken.IsCancellationRequested)
                                return;
                            lock (_onSomethingGate)
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
                        lock (_onSomethingGate)
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

            async ValueTask OnNextAsync(T value, CancellationToken token)
            {
                using var scope = LinkedTokenScope.Create(token, _disposedCancellationToken);
                using (await _onSomethingGate.LockAsync())
                {
                    if (_disposed) return;
                    await _observer.OnNextAsync(value, scope.Token);
                }
            }

            async ValueTask OnErrorResumeAsync(Exception ex, CancellationToken token)
            {
                using var scope = LinkedTokenScope.Create(token, _disposedCancellationToken);
                using (await _onSomethingGate.LockAsync())
                {
                    if (_disposed) return;
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
                lock (_onSomethingGate)
                {
                    _active--;
                    shouldComplete = _active == 0 && _enumerationCompleted;
                }

                return shouldComplete ? CompleteAsync(Result.Success) : default;
            }

            async ValueTask CompleteAsync(Result? result)
            {
                using (await _onSomethingGate.LockAsync())
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
                if (!_reentrant.Value)
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