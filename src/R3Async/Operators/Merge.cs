using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Merge<T>(this AsyncObservable<AsyncObservable<T>> @this) => new MergeObservableObservables<T>(@this);
    public static AsyncObservable<T> Merge<T>(this IEnumerable<AsyncObservable<T>> @this) => new MergeEnumerableObservable<T>(@this);
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
    sealed class MergeSubscription<T> : IAsyncDisposable
    {
        int _innerActiveCount;
        bool _outerCompleted;
        readonly CancellationTokenSource _disposeCts = new();
        readonly CancellationToken _disposedCancellationToken = new();
        readonly SingleAssignmentAsyncDisposable _outerDisposable = new();
        readonly CompositeAsyncDisposable _innerDisposables = new();
        readonly AsyncGate _onSomethingGate = new();
        bool _disposed;
        readonly AsyncObserver<T> _observer;

        public MergeSubscription(AsyncObserver<T> observer)
        {
            _observer = observer;
            _disposedCancellationToken = _disposeCts.Token;
        }

        public async ValueTask SubscribeAsync(AsyncObservable<AsyncObservable<T>> @this, CancellationToken cancellationToken)
        {
            var outerSubscription = await @this.SubscribeAsync((x, _) => SubscribeInnerAsync(x), ForwardOnErrorResume, result =>
            {
                bool shouldComplete;
                lock (_disposeCts)
                {
                    _outerCompleted = true;
                    shouldComplete = _innerActiveCount == 0 || result.IsFailure;
                }

                return shouldComplete ? CompleteAsync(result) : default;
            }, cancellationToken);

            await _outerDisposable.SetDisposableAsync(outerSubscription);
        }

        async ValueTask SubscribeInnerAsync(AsyncObservable<T> inner)
        {
            try
            {
                var innerObserver = new InnerAsyncObserver(this);
                await innerObserver.SubscribeAsync(inner);
            }
            catch (Exception e)
            {
                await CompleteAsync(Result.Failure(e));
            }
        }

        async ValueTask ForwardOnNext(T value, CancellationToken cancellationToken)
        {
            if (_disposed) return;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposedCancellationToken);
            using (await _onSomethingGate.LockAsync())
            {
                if (_disposed) return;
                await _observer.OnNextAsync(value, linkedCts.Token);
            }
        }

        async ValueTask ForwardOnErrorResume(Exception exception, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposedCancellationToken);
            using (await _onSomethingGate.LockAsync())
            {
                if (_disposed) return;
                await _observer.OnErrorResumeAsync(exception, linkedCts.Token);
            }
        }

        async ValueTask CompleteAsync(Result? result)
        {
            lock (_disposeCts)
            {
                if (_disposed) return;
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

        sealed class InnerAsyncObserver(MergeSubscription<T> parent) : AsyncObserver<T>
        {
            public async ValueTask SubscribeAsync(AsyncObservable<T> inner)
            {
                lock (parent._disposeCts)
                {
                    parent._innerActiveCount++;
                }
                await parent._innerDisposables.AddAsync(this);
                await inner.SubscribeAsync(this, parent._disposedCancellationToken);
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
                await parent._innerDisposables.Remove(this);
            }
        }
    }
    sealed class MergeEnumerableObservable<T>(IEnumerable<AsyncObservable<T>> sources) : AsyncObservable<T>
    {
        readonly IEnumerable<AsyncObservable<T>> _sources = sources;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new MergeEnumerableSubscription(observer, _sources);
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
            int _disposed;
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
                            Interlocked.Increment(ref _active);

                            var innerObserver = new InnerAsyncObserver(this);
                            await _innerDisposables.AddAsync(innerObserver);
                            try
                            {
                                await src.SubscribeAsync(innerObserver, _cts.Token);
                            }
                            catch (TaskCanceledException)
                            {
                                return;
                            }
                            catch (Exception ex)
                            {
                                await CompleteAsync(Result.Failure(ex));
                                return;
                            }
                        }

                        if (Volatile.Read(ref _active) == 0)
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
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposedCancellationToken, token);
                using (await _onSomethingGate.LockAsync())
                {
                    if (_disposed == 1) return;
                    await _observer.OnNextAsync(value, linked.Token);
                }
            }

            async ValueTask OnErrorResumeAsync(Exception ex, CancellationToken token)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposedCancellationToken, token);
                using (await _onSomethingGate.LockAsync())
                {
                    if (_disposed == 1) return;
                    await _observer.OnErrorResumeAsync(ex, linked.Token);
                }
            }

            ValueTask OnCompletedAsync(Result result)
            {
                if (result.IsFailure)
                {
                    return CompleteAsync(result);
                }

                if (Interlocked.Decrement(ref _active) == 0)
                {
                    return CompleteAsync(Result.Success);
                }

                return default;
            }

            async ValueTask CompleteAsync(Result? result)
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                {
                    if (result?.Exception is not null and var ex)
                        UnhandledExceptionHandler.OnUnhandledException(ex);
                    return;
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