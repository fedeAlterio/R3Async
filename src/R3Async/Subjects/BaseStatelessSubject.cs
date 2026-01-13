using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects;

public abstract class BaseStatelessSubject<T> : AsyncObservable<T>, ISubject<T>
{
    ImmutableList<AsyncObserver<T>> _observers = [];
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var disposable = AsyncDisposable.Create(() =>
        {
            ImmutableInterlocked.Update(ref _observers, static (observers, observer) => observers.Remove(observer), observer);
            return default;
        });

        ImmutableInterlocked.Update(ref _observers, static (observers, observer) => observers.Add(observer), observer);
        return new(disposable);
    }

    AsyncObservable<T> ISubject<T>.Values => this;
    public ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        return OnNextAsyncCore(Volatile.Read(ref _observers), value, cancellationToken);
    }

    protected abstract ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken);

    public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        return OnErrorResumeAsyncCore(Volatile.Read(ref _observers), error, cancellationToken);
    }

    protected abstract ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken);

    public ValueTask OnCompletedAsync(Result result)
    {
        return OnCompletedAsyncCore(Volatile.Read(ref _observers), result);
    }
    protected abstract ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result);
}