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
        /// Fails with a <see cref="TimeoutException"/> if no value is observed from <paramref name="@this"/>
        /// within <paramref name="dueTime"/>. The window is measured from subscription and reset every time a
        /// value is emitted, so the source only needs to keep producing values more often than <paramref name="dueTime"/>.
        /// </summary>
        /// <param name="dueTime">The maximum allowed quiet period between subscription/values before the stream fails.</param>
        /// <param name="timeProvider">The time provider used to schedule the timeout window. Defaults to <see cref="TimeProvider.System"/>.</param>
        public AsyncObservable<T> Timeout(TimeSpan dueTime, TimeProvider? timeProvider = null)
            => new TimeoutObservable<T>(@this, dueTime, timeProvider ?? TimeProvider.System);
    }
}

internal sealed class TimeoutObservable<T>(AsyncObservable<T> source, TimeSpan dueTime, TimeProvider timeProvider) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var subscription = new TimeoutSubscription(observer, dueTime, timeProvider);
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

    sealed class TimeoutSubscription : IAsyncDisposable
    {
        readonly AsyncObserver<T> _observer;
        readonly TimeSpan _dueTime;
        readonly SingleAssignmentAsyncDisposable _sourceDisposable = new();
        readonly CancellationTokenSource _disposeCts = new();
        readonly CancellationToken _disposeCancellationToken;
        readonly AsyncGate _gate = new();
        readonly TimeProvider _timeProvider;
        readonly SerialAsyncDisposable _timerDisposable = new();
        long _version;
        bool _terminated;

        public TimeoutSubscription(AsyncObserver<T> observer, TimeSpan dueTime, TimeProvider timeProvider)
        {
            _observer = observer;
            _dueTime = dueTime;
            _timeProvider = timeProvider;
            _disposeCancellationToken = _disposeCts.Token;
        }

        public async ValueTask SubscribeAsync(AsyncObservable<T> source, CancellationToken subscriptionToken)
        {
            // The first window opens before subscribing, so a source that takes longer than
            // dueTime to produce its first value times out even if subscription itself is slow.
            long version;
            using (await _gate.LockAsync())
            {
                version = _version;
            }

            await ScheduleTimerAsync(version);
            var subscription = await source.SubscribeAsync(new TimeoutObserver(this), subscriptionToken);
            await _sourceDisposable.SetDisposableAsync(subscription);
        }

        async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
        {
            using var scope = LinkedTokenScope.Create(cancellationToken, _disposeCancellationToken);
            long version;
            using (await _gate.LockAsync())
            {
                if (_terminated) return;
                version = unchecked(++_version);
                await _observer.OnNextAsync(value, scope.Token);
            }

            await ScheduleTimerAsync(version);
        }

        async ValueTask ScheduleTimerAsync(long version)
        {
            var timerSubscription = _timeProvider.CreateTimer(static state =>
            {
                var (self, scheduledVersion) = ((TimeoutSubscription, long))state!;
                self.OnTimerFired(scheduledVersion);
            }, (this, version), _dueTime, System.Threading.Timeout.InfiniteTimeSpan);
            await _timerDisposable.SetDisposableAsync(timerSubscription);
        }

        async void OnTimerFired(long version)
        {
            try
            {
                using (await _gate.LockAsync())
                {
                    if (_terminated || version != _version || _disposeCancellationToken.IsCancellationRequested) return;
                    _terminated = true;
                }

                _disposeCts.Cancel();
                await _timerDisposable.DisposeAsync();
                using (await _gate.LockAsync())
                {
                    await _observer.OnCompletedAsync(Result.Failure(new TimeoutException($"No value was observed within {_dueTime}.")));
                }

                await _sourceDisposable.DisposeAsync();
                _disposeCts.Dispose();
            }
            catch (Exception e)
            {
                UnhandledExceptionHandler.OnUnhandledException(e);
            }
        }

        async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
        {
            using var scope = LinkedTokenScope.Create(cancellationToken, _disposeCancellationToken);
            using (await _gate.LockAsync())
            {
                if (_terminated) return;
                await _observer.OnErrorResumeAsync(error, scope.Token);
            }
        }

        async ValueTask CompleteAsync(Result? result)
        {
            using (await _gate.LockAsync())
            {
                if (_terminated) return;
                _terminated = true;
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

        sealed class TimeoutObserver(TimeoutSubscription subscription) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                => subscription.OnNextAsync(value, cancellationToken);
            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                => subscription.OnErrorResumeAsync(error, cancellationToken);
            protected override ValueTask OnCompletedAsyncCore(Result result)
                => subscription.CompleteAsync(result);
        }
    }
}
