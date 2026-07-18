using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Subjects;

namespace R3Async.Internals;

internal sealed class MulticastAsyncObservable<T>(AsyncObservable<T> observable, ISubject<T> subject) : ConnectableAsyncObservable<T>
{
    readonly AsyncGate _gate = new();
    SingleAssignmentAsyncDisposable? _connection;

    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        return subject.Values.SubscribeAsync(observer.Wrap(), cancellationToken);
    }

    public override async ValueTask<IAsyncDisposable> ConnectAsync(CancellationToken cancellationToken)
    {
        using (await _gate.LockAsync())
        {
            var connection = _connection;
            if (connection is null)
            {
                connection = new SingleAssignmentAsyncDisposable();
                _connection = connection;
                try
                {
                    await connection.SetDisposableAsync(await observable.SubscribeAsync(subject.AsAsyncObserver(), cancellationToken));
                }
                catch
                {
                    _connection = null;
                    await connection.DisposeAsync();
                    throw;
                }
            }

            return CreateDisconnectHandle(connection);
        }
    }

    IAsyncDisposable CreateDisconnectHandle(SingleAssignmentAsyncDisposable connection)
    {
        return AsyncDisposable.Create(async () =>
        {
            using (await _gate.LockAsync())
            {
                // Only the handles of the current connection may disconnect: a stale handle from a
                // previous connection must not tear down a newer one.
                if (_connection != connection)
                    return;

                _connection = null;
                await connection.DisposeAsync();
            }
        });
    }
}
