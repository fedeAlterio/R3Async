using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<AsyncObservable<T>> @this)
    {
        public AsyncObservable<T> Concat() => new ConcatObservable<T>(@this);
    }
}

internal sealed class ConcatObservable<T>(AsyncObservable<AsyncObservable<T>> source) : AsyncObservable<T>
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

    sealed class ConcatSubscription(AsyncObserver<T> observer) : IAsyncDisposable
    {
        readonly Queue<AsyncObservable<T>> _buffer = new();
        readonly CancellationTokenSource _disposeCts = new();
        readonly SingleAssignmentAsyncDisposable _outerDisposable = new();
        readonly SerialDisposable _innerDisposable = new();
        readonly AsyncObserver<T> _observer = observer;
        bool _outerCompleted;
        int _disposed;

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
                await _innerDisposable.SetDisposableAsync(innerSubscription);
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
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _disposeCts.Cancel();
            await _outerDisposable.DisposeAsync();
            await _innerDisposable.DisposeAsync();
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
            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                => subscription._observer.OnErrorResumeAsync(error, cancellationToken);
            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedOuterAsync(result);
        }

        sealed class ConcatInnerObserver(ConcatSubscription subscription) : AsyncObserver<T>
        {
            protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(subscription._disposeCts.Token);
                await subscription._observer.OnNextAsync(value, linkedCts.Token);
            }

            protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(subscription._disposeCts.Token);
                await subscription._observer.OnErrorResumeAsync(error, linkedCts.Token);
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedInnerAsync(result);
        }
    }
}

