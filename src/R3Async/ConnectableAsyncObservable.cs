using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public abstract class ConnectableAsyncObservable<T> : AsyncObservable<T>
{
    public abstract ValueTask<IAsyncDisposable> ConnectAsync(CancellationToken cancellationToken);
}
