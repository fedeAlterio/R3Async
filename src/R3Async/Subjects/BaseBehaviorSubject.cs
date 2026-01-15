using R3Async.Internals;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects;

public abstract class BaseBehaviorSubject<T>(T startValue) : AsyncObservable<T>, ISubject<T>
{
    T _lastValue = startValue;
    readonly AsyncGate _gate = new();
    ImmutableList<AsyncObserver<T>> _observers = [];
    Result? _result;

    AsyncObservable<T> ISubject<T>.Values => this;
    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>> observers;
        using (await _gate.LockAsync())
        {
            if (_result is not null) return;
            _lastValue = value;
            observers = _observers;
        }

        await OnNextAsyncCore(observers, value, cancellationToken);
    }
    protected abstract ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken);


    public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>> observers;
        using (await _gate.LockAsync())
        {
            if (_result is not null) return;
            observers = _observers;
        }

        await OnErrorResumeAsyncCore(observers, error, cancellationToken);
    }
    protected abstract ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken);

    public ValueTask OnCompletedAsync(Result result)
    {
        ImmutableList<AsyncObserver<T>>? observers;
        lock (_gate)
        {
            if (_result is not null) return default;
            _result = result;
            observers = _observers;
            _observers = [];
        }

        return OnCompletedAsyncCore(observers, result);
    }

    protected abstract ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result);

    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result? result;
        using (await _gate.LockAsync())
        {
            result = _result;
            if (result is null)
            {
                _observers = _observers.Add(observer);
                await observer.OnNextAsync(_lastValue, cancellationToken);
            }
        }

        if (result is not null)
        {
            await observer.OnCompletedAsync(result.Value);
            return AsyncDisposable.Empty;
        }

        return AsyncDisposable.Create(async () =>
        {
            using (await _gate.LockAsync())
            {
                _observers = _observers.Remove(observer);
            }
        });
    }
}
