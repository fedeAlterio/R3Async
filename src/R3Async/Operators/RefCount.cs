using R3Async.Internals;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Automatically connects and disconnects <paramref name="source"/> based on its subscriber count: the first
    /// subscriber triggers a call to <see cref="ConnectableAsyncObservable{T}.ConnectAsync"/>, and the connection is
    /// disposed once the last subscriber unsubscribes. A subsequent subscriber after that reconnects <paramref name="source"/> again.
    /// </summary>
    /// <typeparam name="T">The type of the values emitted by <paramref name="source"/>.</typeparam>
    /// <param name="source">The connectable observable to manage the connection of.</param>
    public static AsyncObservable<T> RefCount<T>(this ConnectableAsyncObservable<T> source) => new RefCountObservable<T>(source);

    sealed class RefCountObservable<T>(ConnectableAsyncObservable<T> source) : AsyncObservable<T>
    {
        readonly AsyncGate _gate = new();
        int _refCount;
        SingleAssignmentAsyncDisposable? _connection;

        [DebuggerStepThrough]
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            using(await _gate.LockAsync())
            {
                // incr refCount before Subscribe(completed source decrement refCount in Subscribe)
                ++_refCount;
                bool needConnect = _refCount == 1;
                var coObserver = new RefCountObserver(this, observer);
                var subscription = await source.SubscribeAsync(coObserver, cancellationToken);
                if (needConnect && !coObserver.IsDisposed)
                {
                    SingleAssignmentAsyncDisposable connection = new();
                    _connection = connection;
                    try
                    {
                        await connection.SetDisposableAsync(await source.ConnectAsync(cancellationToken));
                    }
                    catch
                    {
                        await subscription.DisposeAsync();
                        throw;
                    }
                }
                return subscription;
            }
        }

        sealed class RefCountObserver(RefCountObservable<T> parent, AsyncObserver<T> observer) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            {
                return observer.OnNextAsync(value, cancellationToken);
            }

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                return observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override ValueTask OnCompletedAsyncCore(Result result)
            {
                return observer.OnCompletedAsync(result);
            }

            [DebuggerStepThrough]
            protected override async ValueTask DisposeAsyncCore()
            {
                using(await parent._gate.LockAsync())
                {
                    if (--parent._refCount == 0)
                    {
                        var connection = parent._connection;
                        parent._connection = null;
                        if (connection is not null)
                        {
                            await connection.DisposeAsync();
                        }
                    }
                }
            }
        }
    }
}
