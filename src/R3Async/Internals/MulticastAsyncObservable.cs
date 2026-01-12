using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Subjects;

namespace R3Async.Internals;

internal sealed class MulticastAsyncObservable<T>(AsyncObservable<T> observable, ISubject<T> subject) : ConnectableAsyncObservable<T>
{
    readonly AsyncGate _gate = new();
    IAsyncDisposable? _connection;

    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        return subject.Values.SubscribeAsync(observer.Wrap(), cancellationToken);
    }

    public override async ValueTask<IAsyncDisposable> ConnectAsync(CancellationToken cancellationToken)
    {
        using (await _gate.LockAsync())
        {
            if(_connection != null)
            {
                return _connection;
            }

            var connection = await observable.SubscribeAsync(subject.AsAsyncObserver(), cancellationToken);
            _connection = connection;
            return AsyncDisposable.Create(async () =>
            {
                using (await _gate.LockAsync())
                {
                    if (connection is null)
                        return;

                    var localConn = connection;
                    connection = null;
                    await localConn.DisposeAsync();
                }
            });
        }
    }
}
