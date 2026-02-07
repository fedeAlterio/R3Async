using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> TakeUntil<T, TOther>(this AsyncObservable<T> source, AsyncObservable<TOther> other)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (other is null)
            throw new ArgumentNullException(nameof(other));

        return new TakeUntilAsyncObservable<T, TOther>(source, other);
    }

    class TakeUntilAsyncObservable<T, TOther>(AsyncObservable<T> source, AsyncObservable<TOther> other) : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source = source;
        readonly AsyncObservable<TOther> _other = other;

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
                        return parent.ForwardOnErrorResumeAsync(result.Exception!, CancellationToken.None);
                    }

                    return default;
                }
            }
        }
    }
}
