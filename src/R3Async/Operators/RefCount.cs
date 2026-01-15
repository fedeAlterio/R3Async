using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> RefCount<T>(this ConnectableAsyncObservable<T> source) => new RefCountObservable<T>(source);

    sealed class RefCountObservable<T>(ConnectableAsyncObservable<T> source) : AsyncObservable<T>
    {
        readonly AsyncGate _gate = new();
        int _refCount;
        SingleAssignmentAsyncDisposable? _connection;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            using(await _gate.LockAsync())
            {
                // incr refCount before Subscribe(completed source decrement refCxount in Subscribe)
                ++_refCount;
                bool needConnect = _refCount == 1;
                var coObserver = new RefCountObsever(this, observer);
                var subcription = await source.SubscribeAsync(coObserver, cancellationToken);
                if (needConnect && !coObserver.IsDisposed)
                {
                    SingleAssignmentAsyncDisposable connection = new();
                    _connection = connection;
                    await connection.SetDisposableAsync(await source.ConnectAsync(cancellationToken));
                }
                return subcription;
            }
        }

        sealed class RefCountObsever(RefCountObservable<T> parent, AsyncObserver<T> observer) : AsyncObserver<T>
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
