using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async.Subjects;

public abstract class BaseStatelessBehaviorSubject<T>(T startValue) : AsyncObservable<T>, ISubject<T>
{
    readonly T _startValue = startValue;
    T _value = startValue;
    readonly AsyncGate _gate = new();
    ImmutableList<AsyncObserver<T>> _observers = [];
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var disposable = AsyncDisposable.Create(async () =>
        {
            using (await _gate.LockAsync())
            {
                _observers = _observers.Remove(observer);
                if (_observers.Count == 0)
                {
                    _value = _startValue;
                }
            }
        });

        using (await _gate.LockAsync())
        {
            _observers = _observers.Add(observer);
            await observer.OnNextAsync(_value, cancellationToken);
        }

        return disposable;
    }

    AsyncObservable<T> ISubject<T>.Values => this;
    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>> observers;
        using (await _gate.LockAsync())
        {
            _value = value;
            observers = _observers;
        }

        await OnNextAsyncCore(observers, value, cancellationToken);
    }

    protected abstract ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken);

    public async  ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>> observers;
        using (await _gate.LockAsync())
        {
            observers = _observers;
        }

        await OnErrorResumeAsyncCore(observers, error, cancellationToken);
    }

    protected abstract ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken);

    public async ValueTask OnCompletedAsync(Result result)
    {
        ImmutableList<AsyncObserver<T>> observers;
        using (await _gate.LockAsync())
        {
            observers = _observers;
            _observers = [];
            _value = _startValue;
        }

        await OnCompletedAsyncCore(observers, result);
    }
    protected abstract ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result);
}