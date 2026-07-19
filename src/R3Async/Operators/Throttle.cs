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
        /// Emits the first value of each <paramref name="dueTime"/> window and drops the rest until the window expires.
        /// A new window starts the moment a value is emitted.
        /// </summary>
        /// <param name="dueTime">The duration of each throttle window.</param>
        /// <param name="timeProvider">The time provider used to schedule windows. Defaults to <see cref="TimeProvider.System"/>.</param>
        public AsyncObservable<T> ThrottleFirst(TimeSpan dueTime, TimeProvider? timeProvider = null)
            => new ThrottleObservable<T>(@this, dueTime, timeProvider ?? TimeProvider.System, emitFirst: true, emitLast: false);

        /// <summary>
        /// Emits only the latest value received during each <paramref name="dueTime"/> window, emitted once the
        /// window expires. Values are dropped without triggering emission until the window elapses.
        /// </summary>
        /// <param name="dueTime">The duration of each throttle window.</param>
        /// <param name="timeProvider">The time provider used to schedule windows. Defaults to <see cref="TimeProvider.System"/>.</param>
        public AsyncObservable<T> ThrottleLast(TimeSpan dueTime, TimeProvider? timeProvider = null)
            => new ThrottleObservable<T>(@this, dueTime, timeProvider ?? TimeProvider.System, emitFirst: false, emitLast: true);

        /// <summary>
        /// Emits the first value of each <paramref name="dueTime"/> window immediately, and also emits the latest
        /// value observed during that window when it expires (if different from the first).
        /// </summary>
        /// <param name="dueTime">The duration of each throttle window.</param>
        /// <param name="timeProvider">The time provider used to schedule windows. Defaults to <see cref="TimeProvider.System"/>.</param>
        public AsyncObservable<T> ThrottleFirstLast(TimeSpan dueTime, TimeProvider? timeProvider = null)
            => new ThrottleObservable<T>(@this, dueTime, timeProvider ?? TimeProvider.System, emitFirst: true, emitLast: true);
    }
}

internal sealed class ThrottleObservable<T>(AsyncObservable<T> source, TimeSpan dueTime, TimeProvider timeProvider, bool emitFirst, bool emitLast) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var subscription = new ThrottleSubscription(observer, dueTime, timeProvider, emitFirst, emitLast);
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

    sealed class ThrottleSubscription : IAsyncDisposable
    {
        readonly AsyncObserver<T> _observer;
        readonly TimeSpan _dueTime;
        readonly bool _emitFirst;
        readonly bool _emitLast;
        readonly SingleAssignmentAsyncDisposable _sourceDisposable = new();
        readonly CancellationTokenSource _disposeCts = new();
        readonly CancellationToken _disposeCancellationToken;
        readonly AsyncGate _gate = new();
        readonly TimeProvider _timeProvider;
        readonly SerialAsyncDisposable _timerDisposable = new();
        Optional<T> _pending;
        bool _throttling;
        bool _terminated;

        public ThrottleSubscription(AsyncObserver<T> observer, TimeSpan dueTime, TimeProvider timeProvider, bool emitFirst, bool emitLast)
        {
            _observer = observer;
            _dueTime = dueTime;
            _timeProvider = timeProvider;
            _emitFirst = emitFirst;
            _emitLast = emitLast;
            _disposeCancellationToken = _disposeCts.Token;
        }

        public async ValueTask SubscribeAsync(AsyncObservable<T> source, CancellationToken subscriptionToken)
        {
            var subscription = await source.SubscribeAsync(new ThrottleObserver(this), subscriptionToken);
            await _sourceDisposable.SetDisposableAsync(subscription);
        }

        async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
            using (await _gate.LockAsync())
            {
                if (_terminated) return;
                if (_throttling)
                {
                    if (_emitLast) _pending = new Optional<T>(value);
                    return;
                }
                _throttling = true;

                if (_emitFirst)
                {
                    await _observer.OnNextAsync(value, linkedCts.Token);
                }
                else
                {
                    _pending = new Optional<T>(value);
                }
            }

            var timerSubscription = _timeProvider.CreateTimer(static state =>
            {
                var self = (ThrottleSubscription)state!;
                self.OnTimerFired();
            }, this, _dueTime, Timeout.InfiniteTimeSpan);
            await _timerDisposable.SetDisposableAsync(timerSubscription);
        }

        async void OnTimerFired()
        {
            try
            {
                using (await _gate.LockAsync())
                {
                    if (_terminated || _disposeCancellationToken.IsCancellationRequested) return;
                    _throttling = false;
                    var pending = _pending;
                    _pending = Optional<T>.Empty;

                    if (pending.HasValue)
                    {
                        await _observer.OnNextAsync(pending.Value!, _disposeCancellationToken);
                    }
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
                if (_terminated || _disposeCancellationToken.IsCancellationRequested) return;
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

        sealed class ThrottleObserver(ThrottleSubscription subscription) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                => subscription.OnNextAsync(value, cancellationToken);
            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                => subscription.OnErrorResumeAsync(error, cancellationToken);
            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.OnCompletedAsync(result);
        }
    }
}
