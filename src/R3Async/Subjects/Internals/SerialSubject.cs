using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects.Internals;

internal sealed class SerialSubject<T> : BaseSubject<T>
{
    protected override async ValueTask OnNextAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken)
    {
        foreach (var obserevr in observers)
        {
            await obserevr.OnNextAsync(value, cancellationToken);
        }
    }

    protected override async ValueTask OnErrorResumeAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken)
    {
        foreach (var obserevr in observers)
        {
            await obserevr.OnErrorResumeAsync(error, cancellationToken);
        }
    }

    protected override async ValueTask OnCompletedAsyncCore(IReadOnlyList<AsyncObserver<T>> observers, Result result)
    {
        foreach (var obserevr in observers)
        {
            await obserevr.OnCompletedAsync(result);
        }
    }
}