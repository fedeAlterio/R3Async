using R3Async.Internals;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects;


/// <summary>
/// Base class for replaying <see cref="ISubject{T}"/> implementations (BehaviorSubject and replay-latest subjects):
/// a new subscriber immediately receives the last emitted value, if any (via <paramref name="startValue"/> or a
/// subsequent <see cref="OnNextAsync"/> call), before further notifications arrive, or receives the stored completion
/// result if the subject has already completed. Subclasses implement <see cref="OnNextAsyncCore"/>,
/// <see cref="OnErrorResumeAsyncCore"/> and <see cref="OnCompletedAsyncCore"/> to decide how the current observer list
/// is notified (e.g. serially or concurrently).
/// </summary>
/// <param name="startValue">The initial "last value" replayed to subscribers before any value has been pushed; empty for non-behavior replay-latest subjects.</param>
public abstract class BaseReplayLatestSubject<T>(Optional<T> startValue) : AsyncObservable<T>, ISubject<T>
{
    Optional<T> _lastValue = startValue;
    readonly AsyncGate _gate = new();
    ImmutableList<AsyncObserver<T>> _observers = [];
    Result? _result;

    AsyncObservable<T> ISubject<T>.Values => this;

    /// <summary>Pushes a value to all current subscribers and stores it as the latest value replayed to future subscribers. A no-op once the subject has completed.</summary>
    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>> observers;
        using (await _gate.LockAsync())
        {
            if (_result is not null) return;
            _lastValue = new(value);
            observers = _observers;
        }

        await OnNextAsyncCore(observers, value, cancellationToken);
    }
    protected abstract ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken);


    /// <summary>Pushes a resumable error to all current subscribers without terminating the subject. A no-op once the subject has completed.</summary>
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

    /// <summary>Completes the subject with the given <paramref name="result"/>, notifying all current subscribers. Further notifications are ignored.</summary>
    public async ValueTask OnCompletedAsync(Result result)
    {
        ImmutableList<AsyncObserver<T>>? observers;
        using (await _gate.LockAsync())
        {
            if (_result is not null) return;
            _result = result;
            observers = _observers;
            _observers = [];
        }

        await OnCompletedAsyncCore(observers, result);
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
                if (_lastValue.TryGetValue(out var lastValue))
                {
                    await observer.OnNextAsync(lastValue, cancellationToken);
                }
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
