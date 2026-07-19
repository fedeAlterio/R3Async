using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that never emits any value and never completes. Subscribing succeeds
    /// immediately and returns a no-op disposable; the subscription simply stays open until disposed.
    /// </summary>
    public static AsyncObservable<T> Never<T>()
    {
        return NeverAsyncObservable<T>.Instance;
    }

    sealed class NeverAsyncObservable<T> : AsyncObservable<T>
    {
        public static NeverAsyncObservable<T> Instance { get; } = new();
        protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        }
    }
}
