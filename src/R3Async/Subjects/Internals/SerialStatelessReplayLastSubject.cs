using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async.Subjects.Internals;

internal sealed class SerialStatelessReplayLastSubject<T>(Optional<T> startValue) : BaseStatelessReplayLastSubject<T>(startValue)
{
    protected override async ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            await observer.OnNextAsync(value, cancellationToken);
        }
    }

    protected override async ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            await observer.OnErrorResumeAsync(error, cancellationToken);
        }
    }

    protected override async ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result)
    {
        foreach (var observer in observers)
        {
            await observer.OnCompletedAsync(result);
        }
    }
}
