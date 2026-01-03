
using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<AsyncObservable<T>> @this)
    {
        public AsyncObservable<T> Merge() => new MergeObservableObservables<T>(@this);
    }

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
    sealed class MergeSubscription<T>(AsyncObserver<T> observer) : IAsyncDisposable
    {
        int _innerActiveCount;
        bool _outerCompleted;
        readonly CancellationTokenSource _disposeCts = new();
        readonly SingleAssignmentAsyncDisposable _outerDisposable = new();
        readonly CompositeAsyncDisposable _innerDisposables = new();
        readonly AsyncGate _onSomethingGate = new();
        bool _disposed;

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
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            using (await _onSomethingGate.LockAsync())
            {
                await observer.OnNextAsync(value, linkedCts.Token);
            }
        }

        async ValueTask ForwardOnErrorResume(Exception exception, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            using (await _onSomethingGate.LockAsync())
            {
                await observer.OnErrorResumeAsync(exception, linkedCts.Token);
            }
        }

        async ValueTask CompleteAsync(Result? result)
        {
            lock (_disposeCts)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (result is not null)
            {
                await observer.OnCompletedAsync(result.Value);
            }

            _disposeCts.Cancel();
            await _outerDisposable.DisposeAsync();
            await _innerDisposables.DisposeAsync();
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
                await inner.SubscribeAsync(this, parent._disposeCts.Token);
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
}