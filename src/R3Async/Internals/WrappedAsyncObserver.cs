using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals;

internal sealed class WrappedAsyncObserver<T>(AsyncObserver<T> observer) : AsyncObserver<T>
{
    protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken) => observer.OnNextAsync(value, cancellationToken);

    protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken) => observer.OnErrorResumeAsync(error, cancellationToken);

    protected override ValueTask OnCompletedAsyncCore(Result result) => observer.OnCompletedAsync(result);
}