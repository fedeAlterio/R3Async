using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects.Internals;

internal sealed class ConcurrentBehaviorSubject<T>(T startValue) : BaseBehaviorSubject<T>(startValue)
{
    protected override ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken)
    {
        if (observers.Count == 0) return default;

        return new ValueTask(Task.WhenAll(observers.Select(x => x.OnNextAsync(value, cancellationToken).AsTask())));
    }

    protected override ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken)
    {
        if (observers.Count == 0) return default;

        return new ValueTask(Task.WhenAll(observers.Select(x => x.OnErrorResumeAsync(error, cancellationToken).AsTask())));
    }

    protected override ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result)
    {
        if (observers.Count == 0) return default;

        return new ValueTask(Task.WhenAll(observers.Select(x => x.OnCompletedAsync(result).AsTask())));
    }
}
