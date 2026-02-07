using R3Async.Internals;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Helpers;

namespace R3Async;

public sealed record TakeUntilOptions
{
    public bool SourceFailsWhenOtherFails { get; init; }
    public static TakeUntilOptions Default { get; } = new();
}

public delegate IAsyncDisposable TakeUntilStopSignalDelegate(Action<Result> notifyStop);

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> source)
    {
        public AsyncObservable<T> TakeUntil<TOther>(AsyncObservable<TOther> other, TakeUntilOptions? options = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            if (other is null)
                throw new ArgumentNullException(nameof(other));

            return new TakeUntilAsyncObservable<T, TOther>(source, other, options ?? TakeUntilOptions.Default);
        }

        public AsyncObservable<T> TakeUntil(Task task, TakeUntilOptions? options = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            return new TakeUntilTask<T>(source, task, options ?? TakeUntilOptions.Default);
        }

        public AsyncObservable<T> TakeUntil(CancellationToken cancellationToken)
        {
            return new TakeUntilCancellationToken<T>(source, cancellationToken);
        }

        public AsyncObservable<T> TakeUntil(TakeUntilStopSignalDelegate stopSignalSignal, TakeUntilOptions? options = null)
        {
            if (stopSignalSignal is null)
                throw new ArgumentNullException(nameof(stopSignalSignal));

            return new TakeUntilFromRawSignal<T>(source, stopSignalSignal, options ?? TakeUntilOptions.Default);
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
            readonly SingleAssignmentAsyncDisposable _subscription = new();
            readonly SingleAssignmentAsyncDisposable _tokenRegistration = new();
            readonly CancellationToken _disposeCancellationToken;

            public Subscription(TakeUntilCancellationToken<T> parent, AsyncObserver<T> observer)
            {
                _parent = parent;
                _observer = observer;
                _disposeCancellationToken = _cts.Token;
            }

            public async ValueTask SubscribeAsync(CancellationToken cancellationToken)
            {
                var registration = _parent._cancellationToken.Register(OnTokenCanceled);
                await _tokenRegistration.SetDisposableAsync(registration.ToAsyncDisposable());

                var sourceSubscription = await _parent._source.SubscribeAsync(new SourceObserver(this), cancellationToken);
                await _subscription.SetDisposableAsync(sourceSubscription);
            }

            async void OnTokenCanceled()
            {
                try
                {
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
                await Task.Run(_cts.Cancel);
                _cts.Dispose();
                await _subscription.DisposeAsync();
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

    sealed class TakeUntilFromRawSignal<T>(AsyncObservable<T> source, TakeUntilStopSignalDelegate stopSignalSignal, TakeUntilOptions options) : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source = source;
        readonly TakeUntilStopSignalDelegate _stopSignalSignal = stopSignalSignal;
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
            readonly SingleAssignmentAsyncDisposable _subscription = new();
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
                var sourceSubscription = await _parent._source.SubscribeAsync(new SourceObserver(this), cancellationToken);
                await _subscription.SetDisposableAsync(sourceSubscription);
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
                            tcs.SetException(result.Exception);
                        }
                        else
                        {
                            tcs.SetResult(null);
                        }
                    }

                    var disposable = _parent._stopSignalSignal(Stop);

                    try
                    {
                        await tcs.Task.WaitAsync(Timeout.InfiniteTimeSpan, _disposeCancellationToken);
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
                await Task.Run(_cts.Cancel);
                _cts.Dispose();
                await _subscription.DisposeAsync();
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
            readonly SingleAssignmentAsyncDisposable _subscription = new();
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
                var sourceSubscription = await _parent._source.SubscribeAsync(new SourceObserver(this), cancellationToken);
                await _subscription.SetDisposableAsync(sourceSubscription);
            }

            async void WaitAndComplete(Task task)
            {
                try
                {
                    try
                    {
                        await task.WaitAsync(Timeout.InfiniteTimeSpan, _disposeCancellationToken);
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
                await Task.Run(_cts.Cancel);
                _cts.Dispose();
                await _subscription.DisposeAsync();
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
                await Task.Run(_cts.Cancel, _disposeCancellationToken);
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
}
