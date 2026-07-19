using R3Async.Internals;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// Configures how a signal-based <c>TakeUntil</c> overload (other observable, task, or
/// <see cref="CompletionObservableDelegate"/>) reacts when the signal source itself fails.
/// </summary>
public sealed record TakeUntilOptions
{
    /// <summary>
    /// When <see langword="true"/>, a failure of the signal source terminates the resulting observable with
    /// <see cref="Result.Failure(Exception)"/>. When <see langword="false"/> (the default), the failure is
    /// forwarded to the observer via <see cref="AsyncObserver{T}.OnErrorResumeAsync"/> instead, and the source
    /// observable keeps running.
    /// </summary>
    public bool SourceFailsWhenOtherFails { get; init; }

    /// <summary>The default options: <see cref="SourceFailsWhenOtherFails"/> is <see langword="false"/>.</summary>
    public static TakeUntilOptions Default { get; } = new();
}

/// <summary>
/// A factory for a custom completion signal used by <c>TakeUntil</c>. The delegate is invoked once on
/// subscription and must return an <see cref="IAsyncDisposable"/> used to clean up the signal source when the
/// resulting observable completes or is disposed. Call <paramref name="notifyStop"/> to stop the source: pass
/// <see cref="Result.Success"/> to complete it normally, or a failure result according to
/// <see cref="TakeUntilOptions.SourceFailsWhenOtherFails"/>.
/// </summary>
/// <param name="notifyStop">Callback to invoke when the signal fires, triggering completion of the source.</param>
public delegate IAsyncDisposable CompletionObservableDelegate(Action<Result> notifyStop);

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> source)
    {
        /// <summary>
        /// Stops emitting values from <paramref name="source"/> as soon as <paramref name="other"/> emits its
        /// first value or completes successfully.
        /// </summary>
        /// <param name="other">The observable whose first notification (value or successful completion) stops the source.</param>
        /// <param name="options">Controls how a failure of <paramref name="other"/> is handled. Defaults to <see cref="TakeUntilOptions.Default"/>.</param>
        public AsyncObservable<T> TakeUntil<TOther>(AsyncObservable<TOther> other, TakeUntilOptions? options = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            if (other is null)
                throw new ArgumentNullException(nameof(other));

            return new TakeUntilAsyncObservable<T, TOther>(source, other, options ?? TakeUntilOptions.Default);
        }

        /// <summary>Stops emitting values from <paramref name="source"/> as soon as <paramref name="task"/> completes.</summary>
        /// <param name="task">The task whose completion stops the source.</param>
        /// <param name="options">Controls how a failure of <paramref name="task"/> is handled. Defaults to <see cref="TakeUntilOptions.Default"/>.</param>
        public AsyncObservable<T> TakeUntil(Task task, TakeUntilOptions? options = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            return new TakeUntilTask<T>(source, task, options ?? TakeUntilOptions.Default);
        }

        /// <summary>Stops emitting values from <paramref name="source"/> and completes successfully when <paramref name="cancellationToken"/> is canceled.</summary>
        /// <param name="cancellationToken">The token whose cancellation stops the source.</param>
        public AsyncObservable<T> TakeUntil(CancellationToken cancellationToken)
        {
            return new TakeUntilCancellationToken<T>(source, cancellationToken);
        }

        /// <summary>
        /// Stops emitting values from <paramref name="source"/> as soon as <paramref name="predicate"/> returns
        /// <see langword="true"/> for a value. The value that satisfies the predicate is not forwarded to the observer.
        /// </summary>
        /// <param name="predicate">Evaluated for each source value; returning <see langword="true"/> completes the stream.</param>
        public AsyncObservable<T> TakeUntil(Func<T, bool> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            return new TakeUntilPredicate<T>(source, predicate);
        }

        /// <summary>
        /// Stops emitting values from <paramref name="source"/> as soon as <paramref name="asyncPredicate"/>
        /// returns <see langword="true"/> for a value. The value that satisfies the predicate is not forwarded to the observer.
        /// </summary>
        /// <param name="asyncPredicate">Evaluated asynchronously for each source value; returning <see langword="true"/> completes the stream.</param>
        public AsyncObservable<T> TakeUntil(Func<T, CancellationToken, ValueTask<bool>> asyncPredicate)
        {
            if (asyncPredicate is null)
                throw new ArgumentNullException(nameof(asyncPredicate));

            return new TakeUntilAsyncPredicate<T>(source, asyncPredicate);
        }

        /// <summary>
        /// Stops emitting values from <paramref name="source"/> using a custom completion signal built from
        /// <paramref name="stopSignal"/>, for integrating with arbitrary event- or callback-based completion mechanisms.
        /// </summary>
        /// <param name="stopSignal">Factory that sets up the signal source and returns a cleanup disposable. See <see cref="CompletionObservableDelegate"/>.</param>
        /// <param name="options">Controls how a failure signaled via <paramref name="stopSignal"/> is handled. Defaults to <see cref="TakeUntilOptions.Default"/>.</param>
        public AsyncObservable<T> TakeUntil(CompletionObservableDelegate stopSignal, TakeUntilOptions? options = null)
        {
            if (stopSignal is null)
                throw new ArgumentNullException(nameof(stopSignal));

            return new TakeUntilFromRawSignal<T>(source, stopSignal, options ?? TakeUntilOptions.Default);
        }
    }

    sealed class TakeUntilPredicate<T>(AsyncObservable<T> source, Func<T, bool> predicate) : AsyncObservable<T>
    {
        readonly Func<T,bool> _predicate = predicate;
        readonly AsyncObservable<T> _source = source;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new TakeUntilPredicateSubscription(this, observer);
            try
            {
                await subscription.SubscribeAsync(cancellationToken);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class TakeUntilPredicateSubscription(TakeUntilPredicate<T> parent, AsyncObserver<T> observer) : AsyncObserver<T>
        {
            IAsyncDisposable? _subscription;

            public async ValueTask SubscribeAsync(CancellationToken cancellationToken)
            {
                _subscription = await parent._source.SubscribeAsync(this, cancellationToken);
            }

            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                if (parent._predicate(value))
                {
                    return OnCompletedAsyncCore(Result.Success);
                }

                return observer.OnNextAsync(value, cancellationToken);
            }

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                return observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
            {
                return observer.OnCompletedAsync(result);
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                if (_subscription is not null)
                {
                    await _subscription.DisposeAsync();
                }

                await base.DisposeAsyncCore();
            }
        }
    }

    sealed class TakeUntilCancellationToken<T>(AsyncObservable<T> source, CancellationToken cancellationToken) : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source = source;
        readonly CancellationToken _cancellationToken = cancellationToken;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new Subscription(this, observer);
            try
            {
                await subscription.SubscribeAsync(cancellationToken);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class Subscription : IAsyncDisposable
        {
            readonly CancellationTokenSource _cts = new();
            readonly TakeUntilCancellationToken<T> _parent;
            readonly AsyncObserver<T> _observer;
            readonly AsyncGate _gate = new();
            IAsyncDisposable? _subscription;
            IDisposable? _tokenRegistration;
            readonly CancellationToken _disposeCancellationToken;

            public Subscription(TakeUntilCancellationToken<T> parent, AsyncObserver<T> observer)
            {
                _parent = parent;
                _observer = observer;
                _disposeCancellationToken = _cts.Token;
            }

            public async ValueTask SubscribeAsync(CancellationToken cancellationToken)
            {
                _tokenRegistration = _parent._cancellationToken.Register(OnTokenCanceled);
                _subscription = await _parent._source.SubscribeAsync(new SourceObserver(this), cancellationToken);
            }

            async void OnTokenCanceled()
            {
                try
                {
                    await Task.Yield();
                    await ForwardOnCompletedAsync(Result.Success);
                }
                catch
                {
                    // Ignored
                }
            }

            async ValueTask ForwardOnNextAsync(T value, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnNextAsync(value, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnCompletedAsync(Result result)
            {
                using (await _gate.LockAsync())
                {
                    await _observer.OnCompletedAsync(result);
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                _tokenRegistration?.Dispose();
                if (_subscription is not null)
                {
                    await _subscription.DisposeAsync();
                }
                _cts.Dispose();
            }

            sealed class SourceObserver(Subscription parent) : AsyncObserver<T>
            {
                protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnNextAsync(value, cancellationToken);
                }

                protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnErrorResumeAsync(error, cancellationToken);
                }

                protected override ValueTask OnCompletedAsyncCore(Result result)
                {
                    return parent.ForwardOnCompletedAsync(result);
                }
            }
        }
    }

    sealed class TakeUntilFromRawSignal<T>(AsyncObservable<T> source, CompletionObservableDelegate stopSignal, TakeUntilOptions options) : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source = source;
        readonly CompletionObservableDelegate _stopSignal = stopSignal;
        readonly TakeUntilOptions _options = options;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new Subscription(this, observer);
            try
            {
                await subscription.SubscribeAsync(cancellationToken);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class Subscription : IAsyncDisposable
        {
            readonly CancellationTokenSource _cts = new();
            readonly TakeUntilFromRawSignal<T> _parent;
            readonly AsyncObserver<T> _observer;
            readonly AsyncGate _gate = new();
            IAsyncDisposable? _subscription;
            readonly CancellationToken _disposeCancellationToken;

            public Subscription(TakeUntilFromRawSignal<T> parent, AsyncObserver<T> observer)
            {
                _parent = parent;
                _observer = observer;
                _disposeCancellationToken = _cts.Token;
            }

            public async ValueTask SubscribeAsync(CancellationToken cancellationToken)
            {
                WaitAndComplete();
                _subscription = await _parent._source.SubscribeAsync(new SourceObserver(this), cancellationToken);
            }

            async void WaitAndComplete()
            {
                try
                {
                    var tcs = new TaskCompletionSource<object?>();

                    void Stop(Result result)
                    {
                        if (result.IsFailure)
                        {
                            tcs.TrySetException(result.Exception);
                        }
                        else
                        {
                            tcs.TrySetResult(null);
                        }
                    }

                    var disposable = _parent._stopSignal(Stop);

                    try
                    {
                        await tcs.Task.WaitAsync(System.Threading.Timeout.InfiniteTimeSpan, _disposeCancellationToken);
                        try
                        {
                            await disposable.DisposeAsync();
                        }
                        catch
                        {
                            // Ignored
                        }
                        await ForwardOnCompletedAsync(Result.Success);
                    }
                    catch (Exception e)
                    {
                        try
                        {
                            await disposable.DisposeAsync();
                        }
                        catch
                        {
                            // Ignored
                        }

                        if (_parent._options.SourceFailsWhenOtherFails)
                        {
                            await ForwardOnCompletedAsync(Result.Failure(e));
                        }
                        else
                        {
                            await ForwardOnErrorResumeAsync(e, CancellationToken.None);
                        }
                    }
                }
                catch
                {
                    // Ignored
                }
            }

            async ValueTask ForwardOnNextAsync(T value, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnNextAsync(value, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnCompletedAsync(Result result)
            {
                using (await _gate.LockAsync())
                {
                    await _observer.OnCompletedAsync(result);
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                if (_subscription is not null)
                {
                    await _subscription.DisposeAsync();
                }
                _cts.Dispose();
            }

            sealed class SourceObserver(Subscription parent) : AsyncObserver<T>
            {
                protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnNextAsync(value, cancellationToken);
                }

                protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnErrorResumeAsync(error, cancellationToken);
                }

                protected override ValueTask OnCompletedAsyncCore(Result result)
                {
                    return parent.ForwardOnCompletedAsync(result);
                }
            }
        }
    }

    sealed class TakeUntilTask<T>(AsyncObservable<T> source, Task task, TakeUntilOptions options) : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source = source;
        readonly Task _task = task;
        readonly TakeUntilOptions _options = options;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new Subscription(this, observer);
            try
            {
                await subscription.SubscribeAsync(cancellationToken);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class Subscription : IAsyncDisposable
        {
            readonly CancellationTokenSource _cts = new();
            readonly TakeUntilTask<T> _parent;
            readonly AsyncObserver<T> _observer;
            readonly AsyncGate _gate = new();
            IAsyncDisposable? _subscription;
            readonly CancellationToken _disposeCancellationToken;

            public Subscription(TakeUntilTask<T> parent, AsyncObserver<T> observer)
            {
                _parent = parent;
                _observer = observer;
                _disposeCancellationToken = _cts.Token;
            }

            public async ValueTask SubscribeAsync(CancellationToken cancellationToken)
            {
                var task = _parent._task;
                WaitAndComplete(task);
                _subscription = await _parent._source.SubscribeAsync(new SourceObserver(this), cancellationToken);
            }

            async void WaitAndComplete(Task task)
            {
                try
                {
                    try
                    {
                        await task.WaitAsync(System.Threading.Timeout.InfiniteTimeSpan, _disposeCancellationToken);
                        await ForwardOnCompletedAsync(Result.Success);
                    }
                    catch (Exception e)
                    {
                        if (_parent._options.SourceFailsWhenOtherFails)
                        {
                            await ForwardOnCompletedAsync(Result.Failure(e));
                        }
                        else
                        {
                            await ForwardOnErrorResumeAsync(e, CancellationToken.None);
                        }
                    }
                }
                catch 
                {
                    // Ignored
                }
            }

            async ValueTask ForwardOnNextAsync(T value, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnNextAsync(value, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnCompletedAsync(Result result)
            {
                using (await _gate.LockAsync())
                {
                    await _observer.OnCompletedAsync(result);
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                if (_subscription is not null)
                {
                    await _subscription.DisposeAsync();
                }
                _cts.Dispose();
            }

            sealed class SourceObserver(Subscription parent) : AsyncObserver<T>
            {
                protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnNextAsync(value, cancellationToken);
                }

                protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnErrorResumeAsync(error, cancellationToken);
                }

                protected override ValueTask OnCompletedAsyncCore(Result result)
                {
                    return parent.ForwardOnCompletedAsync(result);
                }
            }
        }
    }

    sealed class TakeUntilAsyncObservable<T, TOther>(AsyncObservable<T> source, AsyncObservable<TOther> other, TakeUntilOptions options) : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source = source;
        readonly AsyncObservable<TOther> _other = other;
        readonly TakeUntilOptions _options = options;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new Subscription(this, observer);
            try
            {
                await subscription.SubscribeAsync(cancellationToken);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class Subscription : IAsyncDisposable
        {
            readonly TakeUntilAsyncObservable<T, TOther> _parent;
            readonly AsyncObserver<T> _observer;
            readonly AsyncGate _gate = new();
            readonly SingleAssignmentAsyncDisposable _disposable = new();
            readonly SingleAssignmentAsyncDisposable _otherDisposable = new();
            readonly CancellationTokenSource _cts = new();
            readonly CancellationToken _disposeCancellationToken;

            public Subscription(TakeUntilAsyncObservable<T, TOther> parent, AsyncObserver<T> observer)
            {
                _parent = parent;
                _observer = observer;
                _disposeCancellationToken = _cts.Token;
            }

            public async ValueTask<IAsyncDisposable> SubscribeAsync(CancellationToken cancellationToken)
            {
                var otherSubscription = await _parent._other.SubscribeAsync(new OtherObserver(this), cancellationToken);
                await _otherDisposable.SetDisposableAsync(otherSubscription);

                var sourceSubscription = await _parent._source.SubscribeAsync(new FirstSubscription(this), cancellationToken);
                await _disposable.SetDisposableAsync(sourceSubscription);

                return this;
            }

            async ValueTask ForwardOnNextAsync(T value, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnNextAsync(value, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationToken, cancellationToken);
                using (await _gate.LockAsync())
                {
                    await _observer.OnErrorResumeAsync(error, linkedCts.Token);
                }
            }

            async ValueTask ForwardOnCompletedAsync(Result result)
            {
                using (await _gate.LockAsync())
                {
                    await _observer.OnCompletedAsync(result);
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                await _otherDisposable.DisposeAsync();
                await _disposable.DisposeAsync();
                _cts.Dispose();
            }

            sealed class FirstSubscription(Subscription parent) : AsyncObserver<T>
            {
                protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnNextAsync(value, cancellationToken);
                }

                protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnErrorResumeAsync(error, cancellationToken);
                }

                protected override ValueTask OnCompletedAsyncCore(Result result)
                {
                    return parent.ForwardOnCompletedAsync(result);
                }
            }

            sealed class OtherObserver(Subscription parent) : AsyncObserver<TOther>
            {
                protected override async ValueTask OnNextAsyncCore(TOther value, CancellationToken cancellationToken)
                {
                    await parent.ForwardOnCompletedAsync(Result.Success);
                    await DisposeAsync();
                }

                protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                {
                    return parent.ForwardOnErrorResumeAsync(error, cancellationToken);
                }

                protected override ValueTask OnCompletedAsyncCore(Result result)
                {
                    if (result.IsFailure)
                    {
                        if (parent._parent._options.SourceFailsWhenOtherFails)
                        {
                            return parent.ForwardOnCompletedAsync(result);
                        }

                        return parent.ForwardOnCompletedAsync(Result.Success);
                    }

                    return default;
                }
            }
        }
    }

    sealed class TakeUntilAsyncPredicate<T>(AsyncObservable<T> source, Func<T, CancellationToken, ValueTask<bool>> asyncPredicate) : AsyncObservable<T>
    {
        readonly Func<T, CancellationToken, ValueTask<bool>> _asyncPredicate = asyncPredicate;
        readonly AsyncObservable<T> _source = source;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var subscription = new TakeUntilAsyncPredicateSubscription(this, observer);
            try
            {
                await subscription.SubscribeAsync(cancellationToken);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class TakeUntilAsyncPredicateSubscription(TakeUntilAsyncPredicate<T> parent, AsyncObserver<T> observer) : AsyncObserver<T>
        {
            IAsyncDisposable? _subscription;

            public async ValueTask SubscribeAsync(CancellationToken cancellationToken)
            {
                _subscription = await parent._source.SubscribeAsync(this, cancellationToken);
            }

            protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                if (await parent._asyncPredicate(value, cancellationToken))
                {
                    await OnCompletedAsyncCore(Result.Success);
                    return;
                }
                await observer.OnNextAsync(value, cancellationToken);
            }

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                return observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
            {
                return observer.OnCompletedAsync(result);
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                if (_subscription is not null)
                {
                    await _subscription.DisposeAsync();
                }
                await base.DisposeAsyncCore();
            }
        }
    }
}
