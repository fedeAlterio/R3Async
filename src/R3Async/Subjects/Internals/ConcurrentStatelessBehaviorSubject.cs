using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects.Internals;

internal sealed class ConcurrentStatelessBehaviorSubject<T>(T startValue) : BaseStatelessBehaviorSubject<T>(startValue)
{
    protected override ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken)
    {
        return Helpers.ForwardOnNextConcurrently(observers, value, cancellationToken);
    }

    protected override ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken)
    {
        return Helpers.ForwardOnErrorResumeConcurrently(observers, error, cancellationToken);
    }

    protected override ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result)
    {
        return Helpers.ForwardOnCompletedConcurrently(observers, result);
    }
}
