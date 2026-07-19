using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects;

/// <summary>
/// Base class for non-replaying <see cref="ISubject{T}"/> implementations: new subscribers receive nothing until the
/// next notification (or, if the subject has already completed, immediately receive the stored completion result).
/// Subclasses implement <see cref="OnNextAsyncCore"/>, <see cref="OnErrorResumeAsyncCore"/> and
/// <see cref="OnCompletedAsyncCore"/> to decide how the current observer list is notified (e.g. serially or concurrently).
/// </summary>
public abstract class BaseSubject<T> : AsyncObservable<T>, ISubject<T>
{
    ImmutableList<AsyncObserver<T>> _observers = [];
    readonly object _gate = new();
    Result? _result;

    AsyncObservable<T> ISubject<T>.Values => this;
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result? result;

        lock (_gate)
        {
            result = _result;
            if (result is null)
            {
                _observers = _observers.Add(observer);
            }
        }

        if (result is not null)
        {
            await observer.OnCompletedAsync(result.Value);
            return AsyncDisposable.Empty;
        }

        return AsyncDisposable.Create(() =>
        {
            lock (_gate)
            {
                _observers = _observers.Remove(observer);
            }
            return default;
        });
    }

    /// <summary>Pushes a value to all current subscribers. A no-op once the subject has completed.</summary>
    public ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>>? observers;

        lock (_gate)
        {
            if (_result is not null) return default;
            observers = _observers;
        }

        return OnNextAsyncCore(observers, value, cancellationToken);
    }
    protected abstract ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken);

    /// <summary>Pushes a resumable error to all current subscribers without terminating the subject. A no-op once the subject has completed.</summary>
    public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        ImmutableList<AsyncObserver<T>>? observers;

        lock (_gate)
        {
            if (_result is not null) return default;
            observers = _observers;
        }

        return OnErrorResumeAsyncCore(observers, error, cancellationToken);
    }
    protected abstract ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken);


    /// <summary>Completes the subject with the given <paramref name="result"/>, notifying all current subscribers. Further notifications are ignored.</summary>
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
}