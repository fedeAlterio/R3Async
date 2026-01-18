using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Concat<T>(this AsyncObservable<AsyncObservable<T>> @this) => new ConcatObservablesObservable<T>(@this);
    public static AsyncObservable<T> Concat<T>(this IEnumerable<AsyncObservable<T>> @this) => new ConcatEnumerableObservable<T>(@this);
    public static AsyncObservable<T> Concat<T>(this AsyncObservable<T> @this, AsyncObservable<T> second) => new ConcatEnumerableObservable<T>([@this, second]);
}

internal sealed class ConcatEnumerableObservable<T>(IEnumerable<AsyncObservable<T>> observables) : AsyncObservable<T>
{
    readonly IEnumerable<AsyncObservable<T>> _observables = observables;

    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var subscription = new ConcatEnumerableSubscription(this, observer);
        try
        {
            await subscription.SubscribeNextAsync();
        }
        catch 
        {
            await subscription.DisposeAsync();
            throw;
        }

        return subscription;
    }

    sealed class ConcatEnumerableSubscription : IAsyncDisposable
    {
        readonly IEnumerator<AsyncObservable<T>> _enumerator;
        readonly SerialAsyncDisposable _innerDisposable = new();
        readonly CancellationTokenSource _cts = new();
        readonly CancellationToken _disposedCancellationToken;
        int _disposed;
        readonly AsyncObserver<T> _observer;

        public ConcatEnumerableSubscription(ConcatEnumerableObservable<T> parent, AsyncObserver<T> observer)
        {
            _observer = observer;
            _enumerator = parent._observables.GetEnumerator();
            _disposedCancellationToken = _cts.Token;
        }

        public async ValueTask SubscribeNextAsync()
        {
            try
            {
                if (_enumerator.MoveNext())
                {
                    var current = _enumerator.Current;
                    var subscription = await current!.SubscribeAsync(OnInnerNextAsync, OnInnerErrorResumeAsync,
                                                                     result => result.IsFailure 
                                                                         ? CompleteAsync(result)
                                                                         : SubscribeNextAsync(),
                                                                     _cts.Token);

                    await _innerDisposable.SetDisposableAsync(subscription);
                }
                else
                {
                    await CompleteAsync(Result.Success);
                }
            }
            catch (Exception e)
            {
                await CompleteAsync(Result.Failure(e));
            }
        }

        async ValueTask OnInnerErrorResumeAsync(Exception exception, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCancellationToken, cancellationToken);
            await _observer.OnErrorResumeAsync(exception, linkedCts.Token);
        }

        async ValueTask OnInnerNextAsync(T value, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCancellationToken, cancellationToken);
            await _observer.OnNextAsync(value, linkedCts.Token);
        }

        async ValueTask CompleteAsync(Result? result)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                if (result?.Exception is not null and var exception)
                {
                    UnhandledExceptionHandler.OnUnhandledException(exception);
                }

                return;
            }
            
            _cts.Cancel();
            await _innerDisposable.DisposeAsync();
            if (result is not null)
            {
                await _observer.OnCompletedAsync(result.Value);
            }
            _enumerator.Dispose();
            _cts.Dispose();
        }

        public ValueTask DisposeAsync() => CompleteAsync(null);
    }
}

internal sealed class ConcatObservablesObservable<T>(AsyncObservable<AsyncObservable<T>> source) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var subscription = new ConcatSubscription(observer);
        try
        {
            await subscription.SubscribeAsync(source, cancellationToken);
        }
        catch
        {
            await subscription.DisposeAsync();
            throw;
        }

        return subscription;
    }

    sealed class ConcatSubscription : IAsyncDisposable
    {
        readonly Queue<AsyncObservable<T>> _buffer = new();
        readonly CancellationTokenSource _disposeCts = new();
        readonly CancellationToken _disposedCancellationToken;
        readonly SingleAssignmentAsyncDisposable _outerDisposable = new();
        readonly SerialAsyncDisposable _innerSubscription = new();
        readonly AsyncObserver<T> _observer;
        readonly AsyncGate _observerOnSomethingGate = new();
        bool _outerCompleted;
        int _disposed;

        public ConcatSubscription(AsyncObserver<T> observer)
        {
            _observer = observer;
            _disposedCancellationToken = _disposeCts.Token;
        }

        public async ValueTask SubscribeAsync(AsyncObservable<AsyncObservable<T>> source, CancellationToken subscriptionToken)
        {
            var outerSubscription = await source.SubscribeAsync(new ConcatOuterObserver(this), subscriptionToken);
            await _outerDisposable.SetDisposableAsync(outerSubscription);
        }

        public ValueTask OnNextOuterAsync(AsyncObservable<T> inner)
        {
            bool shouldSubscribe = false;
            lock (_buffer)
            {
                _buffer.Enqueue(inner);
                if (_buffer.Count == 1)
                {
                    shouldSubscribe = true;
                }
            }

            if (shouldSubscribe)
            {
                return SubscribeToInnerLoop(inner);
            }

            return default;
        }

        public ValueTask OnCompletedOuterAsync(Result result)
        {
            bool shouldComplete = false;
            Result? completeResult = null;
            lock (_buffer)
            {
                _outerCompleted = true;
                if (result.IsFailure || _buffer.Count == 0)
                {
                    shouldComplete = true;
                    completeResult = result;
                }
            }

            return shouldComplete ? CompleteAsync(completeResult) : default;
        }

        async ValueTask SubscribeToInnerLoop(AsyncObservable<T> currentInner)
        {
            try
            {
                var innerSubscription = await currentInner.SubscribeAsync(new ConcatInnerObserver(this), _disposeCts.Token);
                await _innerSubscription.SetDisposableAsync(innerSubscription);
            }
            catch (Exception e)
            {
                await CompleteAsync(Result.Failure(e));
            }
        }

        public ValueTask OnCompletedInnerAsync(Result result)
        {
            if (result.IsFailure)
            {
                return CompleteAsync(result);
            }

            AsyncObservable<T>? nextInner;
            bool outerCompleted;
            lock (_buffer)
            {
                _buffer.Dequeue();
                _buffer.TryPeek(out nextInner);
                outerCompleted = _outerCompleted;
            }

            if (nextInner is null)
            {
                return outerCompleted ? CompleteAsync(Result.Success) : default;
            }

            return SubscribeToInnerLoop(nextInner);
        }


        async ValueTask CompleteAsync(Result? result)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                if (result?.Exception is not null and var exception)
                {
                    UnhandledExceptionHandler.OnUnhandledException(exception);
                }

                return;
            }

            _disposeCts.Cancel();
            await _innerSubscription.DisposeAsync();
            await _outerDisposable.DisposeAsync();
            if (result is not null)
            {
                await _observer.OnCompletedAsync(result.Value);
            }
            _disposeCts.Dispose();
        }

        public ValueTask DisposeAsync() => CompleteAsync(null);

        sealed class ConcatOuterObserver(ConcatSubscription subscription) : AsyncObserver<AsyncObservable<T>>
        {
            protected override ValueTask OnNextAsyncCore(AsyncObservable<T> value, CancellationToken cancellationToken)
                => subscription.OnNextOuterAsync(value);
            protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(subscription._disposedCancellationToken, cancellationToken);
                using (await subscription._observerOnSomethingGate.LockAsync())
                {
                    await subscription._observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedOuterAsync(result);
        }

        sealed class ConcatInnerObserver(ConcatSubscription subscription) : AsyncObserver<T>
        {
            protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(subscription._disposedCancellationToken, cancellationToken);
                using (await subscription._observerOnSomethingGate.LockAsync())
                {
                    await subscription._observer.OnNextAsync(value, linkedCts.Token);
                }
            }

            protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(subscription._disposedCancellationToken, cancellationToken);
                using (await subscription._observerOnSomethingGate.LockAsync())
                {
                    await subscription._observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedInnerAsync(result);
        }
    }
}

