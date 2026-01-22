using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<AsyncObservable<T>> @this)
    {
        public AsyncObservable<T> Switch() => new SwitchObservable<T>(@this);
    }
}

internal sealed class SwitchObservable<T>(AsyncObservable<AsyncObservable<T>> source) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var subscription = new SwitchSubscription(observer);
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

    sealed class SwitchSubscription : IAsyncDisposable
    {
        readonly AsyncObserver<T> _observer;
        readonly SingleAssignmentAsyncDisposable _outerDisposable = new();
        readonly CancellationTokenSource _disposeCts = new();
        readonly CancellationToken _disposeCancellationToken;
        IAsyncDisposable? _currentInnerSubscription;

        readonly object _gate = new();
        readonly AsyncGate _observerOnSomethingGate = new();
        bool _outerCompleted;
        bool _disposed;

        public SwitchSubscription(AsyncObserver<T> observer)
        {
            _observer = observer;
            _disposeCancellationToken = _disposeCts.Token;
        }

        public async ValueTask SubscribeAsync(AsyncObservable<AsyncObservable<T>> source, CancellationToken subscriptionToken)
        {
            var outerSubscription = await source.SubscribeAsync(new SwitchOuterObserver(this), subscriptionToken);
            await _outerDisposable.SetDisposableAsync(outerSubscription);
        }

        public ValueTask OnNextOuterAsync(AsyncObservable<T> inner)
        {
            IAsyncDisposable? previousSubscription;
            lock (_gate)
            {
                previousSubscription = _currentInnerSubscription;
                _currentInnerSubscription = null;
            }

            return SubscribeToInnerAfterDisposingPrevious(inner, previousSubscription);
        }

        async ValueTask SubscribeToInnerAfterDisposingPrevious(AsyncObservable<T> inner, IAsyncDisposable? previousSubscription)
        {
            try
            {
                if (previousSubscription is not null)
                {
                    try
                    {
                        await previousSubscription.DisposeAsync();
                    }
                    catch (Exception e)
                    {
                        await CompleteAsync(Result.Failure(e));
                        return;
                    }
                }

                var innerObserver = new SwitchInnerObserver(this);
                var innerSubscription = await inner.SubscribeAsync(innerObserver, _disposeCancellationToken);
                bool shouldDispose = false;
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _currentInnerSubscription = innerSubscription;
                    }
                    else
                    {
                        shouldDispose = true;
                    }
                }

                if (shouldDispose)
                {
                    await innerSubscription.DisposeAsync();
                }
            }
            catch (Exception e)
            {
                await CompleteAsync(Result.Failure(e));
            }
        }

        public ValueTask OnCompletedOuterAsync(Result result)
        {
            if (result.IsFailure)
            {
                return CompleteAsync(result);
            }

            bool shouldComplete;
            lock (_gate)
            {
                _outerCompleted = true;
                shouldComplete = _currentInnerSubscription is null;
            }

            return shouldComplete ? CompleteAsync(Result.Success) : default;
        }

        public ValueTask OnCompletedInnerAsync( Result result)
        {
            Result? actualResult = null;
            lock (_gate)
            {
                _currentInnerSubscription = null;
                if (result.IsFailure)
                {
                    actualResult = result;
                }
                else if (_outerCompleted)
                {
                    actualResult = Result.Success;
                }
            }

            return actualResult is not null ? CompleteAsync(actualResult) : default;
        }

        public async ValueTask OnNextInnerAsync(T value, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
            using (await _observerOnSomethingGate.LockAsync())
            {
                await _observer.OnNextAsync(value, linkedCts.Token);
            }
        }

        public async ValueTask OnErrorInnerAsync(Exception error, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
            using (await _observerOnSomethingGate.LockAsync())
            {
                await _observer.OnErrorResumeAsync(error, linkedCts.Token);
            }
        }

        async ValueTask CompleteAsync(Result? result)
        {
            IAsyncDisposable? toDispose;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                toDispose = _currentInnerSubscription;
                _currentInnerSubscription = null;
            }

            _disposeCts.Cancel();
            if (toDispose is not null)
            {
                await toDispose.DisposeAsync();
            }
            await _outerDisposable.DisposeAsync();

            if (result is not null)
            {
                await _observer.OnCompletedAsync(result.Value);
            }

            _disposeCts.Dispose();
        }

        public ValueTask DisposeAsync() => CompleteAsync(null);

        sealed class SwitchOuterObserver(SwitchSubscription subscription) : AsyncObserver<AsyncObservable<T>>
        {
            protected override ValueTask OnNextAsyncCore(AsyncObservable<T> value, CancellationToken cancellationToken)
                => subscription.OnNextOuterAsync(value);
            protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(subscription._disposeCancellationToken, cancellationToken);
                using (await subscription._observerOnSomethingGate.LockAsync())
                {
                    await subscription._observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }
            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedOuterAsync(result);
        }

        sealed class SwitchInnerObserver(SwitchSubscription subscription) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                => subscription.OnNextInnerAsync(value, cancellationToken);
            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                => subscription.OnErrorInnerAsync(error, cancellationToken);
            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedInnerAsync(result);
        }
    }
}
