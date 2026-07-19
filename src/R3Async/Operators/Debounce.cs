using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Suppresses values that are followed by another value within <paramref name="dueTime"/>; only emits a
        /// value once <paramref name="dueTime"/> has elapsed without a newer value arriving (i.e. after a quiet
        /// period). Known as <c>Throttle</c> in classic Rx.NET. If the source completes successfully while a
        /// value is pending, the pending value is flushed before completion.
        /// </summary>
        /// <param name="dueTime">The quiet period that must elapse after a value before it is emitted.</param>
        /// <param name="timeProvider">The time provider used to schedule the quiet period. Defaults to <see cref="TimeProvider.System"/>.</param>
        public AsyncObservable<T> Debounce(TimeSpan dueTime, TimeProvider? timeProvider = null)
            => new DebounceObservable<T>(@this, dueTime, timeProvider ?? TimeProvider.System);
    }
}

internal sealed class DebounceObservable<T>(AsyncObservable<T> source, TimeSpan dueTime, TimeProvider timeProvider) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var subscription = new DebounceSubscription(observer, dueTime, timeProvider);
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

    sealed class DebounceSubscription : IAsyncDisposable
    {
        readonly AsyncObserver<T> _observer;
        readonly TimeSpan _dueTime;
        readonly SingleAssignmentAsyncDisposable _sourceDisposable = new();
        readonly CancellationTokenSource _disposeCts = new();
        readonly CancellationToken _disposeCancellationToken;
        readonly AsyncGate _gate = new();
        readonly TimeProvider _timeProvider;
        readonly SerialAsyncDisposable _timerDisposable = new();
        Optional<T> _pending;
        long _version;
        bool _sourceCompleted;
        bool _terminated;

        public DebounceSubscription(AsyncObserver<T> observer, TimeSpan dueTime, TimeProvider timeProvider)
        {
            _observer = observer;
            _dueTime = dueTime;
            _timeProvider = timeProvider;
            _disposeCancellationToken = _disposeCts.Token;
        }

        public async ValueTask SubscribeAsync(AsyncObservable<T> source, CancellationToken subscriptionToken)
        {
            var subscription = await source.SubscribeAsync(new DebounceObserver(this), subscriptionToken);
            await _sourceDisposable.SetDisposableAsync(subscription);
        }

        async ValueTask OnNextAsync(T value)
        {
            long version;
            using (await _gate.LockAsync())
            {
                if (_terminated || _sourceCompleted) return;
                _pending = new Optional<T>(value);
                version = unchecked(++_version);
            }

            var timerSubscription = _timeProvider.CreateTimer(static state =>
            {
                var (self, scheduledVersion) = ((DebounceSubscription, long))state!;
                self.OnTimerFired(scheduledVersion);
            }, (this, version), _dueTime, Timeout.InfiniteTimeSpan);
            await _timerDisposable.SetDisposableAsync(timerSubscription);
        }

        async void OnTimerFired(long version)
        {
            try
            {
                using (await _gate.LockAsync())
                {
                    if (_terminated || _sourceCompleted || !_pending.HasValue || _disposeCancellationToken.IsCancellationRequested) return;
                    if (version != _version) return;
                    var value = _pending.Value!;
                    _pending = Optional<T>.Empty;

                    await _observer.OnNextAsync(value, _disposeCancellationToken);
                }
            }
            catch (Exception e)
            {
                UnhandledExceptionHandler.OnUnhandledException(e);
            }
        }

        async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
            using (await _gate.LockAsync())
            {
                await _observer.OnErrorResumeAsync(error, linkedCts.Token);
            }
        }

        ValueTask OnCompletedAsync(Result result) => result.IsFailure ? CompleteAsync(result) : FlushAndCompleteAsync();

        async ValueTask FlushAndCompleteAsync()
        {
            await _timerDisposable.DisposeAsync();
            using (await _gate.LockAsync())
            {
                if (_terminated || _sourceCompleted || _disposeCancellationToken.IsCancellationRequested) return;
                _sourceCompleted = true;
                _terminated = true;
                var pending = _pending;
                _pending = Optional<T>.Empty;

                if (pending.HasValue)
                {
                    await _observer.OnNextAsync(pending.Value!, _disposeCancellationToken);
                }

                await _observer.OnCompletedAsync(Result.Success);
            }

            await _sourceDisposable.DisposeAsync();
            _disposeCts.Dispose();
        }

        async ValueTask CompleteAsync(Result? result)
        {
            using (await _gate.LockAsync())
            {
                if (_terminated) return;
                _terminated = true;
                _pending = Optional<T>.Empty;
            }

            _disposeCts.Cancel();
            await _timerDisposable.DisposeAsync();
            if (result is not null)
            {
                using (await _gate.LockAsync())
                {
                    await _observer.OnCompletedAsync(result.Value);
                }
            }
            await _sourceDisposable.DisposeAsync();
            _disposeCts.Dispose();
        }

        public ValueTask DisposeAsync() => CompleteAsync(null);

        sealed class DebounceObserver(DebounceSubscription subscription) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                => subscription.OnNextAsync(value);
            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                => subscription.OnErrorResumeAsync(error, cancellationToken);
            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedAsync(result);
        }
    }
}
