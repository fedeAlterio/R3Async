namespace R3Async.Tests;

internal sealed class ManualSource<T> : AsyncObservable<T>
{
    public AsyncObserver<T>? Observer { get; private set; }
    public bool Disposed { get; private set; }

    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        Observer = observer;
        return new(AsyncDisposable.Create(() => { Disposed = true; }));
    }
}
