using R3Async.Internals;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public class RefCountLazy<T>(Func<CancellationToken, ValueTask<AsyncDisposableValue<T>>> valueFactory)
{
    readonly AsyncGate _gate = new();

    Connection? _connection;
    public async ValueTask<IAsyncDisposableReference<T>> GetAsync(CancellationToken cancellationToken)
    {
        using (await _gate.LockAsync())
        {
            while (true)
            {
                var connection = _connection;

                if (connection is null)
                {
                    connection = new Connection(this, valueFactory);
                    _connection = connection;
                }
                await connection.IncrementRefCount(cancellationToken);
                if (_connection == connection) return new Reference(connection);
            }
        }
    }
    internal sealed class Connection(RefCountLazy<T> parent, Func<CancellationToken, ValueTask<AsyncDisposableValue<T>>> valueFactory)

    {
        int _refCount;
        public async ValueTask DecrementRefCount()
        {
            using (await parent._gate.LockAsync())
            {
                if (--_refCount == 0)
                {
                    parent._connection = null;
                    await Entry!.Value.Disposable.DisposeAsync();
                }
            }
        }

        public async ValueTask IncrementRefCount(CancellationToken cancellationToken)
        {
            _refCount++;
            if (Entry is not null) return;

            try
            {
                Entry = await valueFactory(cancellationToken);
            }
            catch
            {
                // The factory failed, so no Reference is handed out for this increment and the
                // count can never reach zero again. Drop the connection so the next GetAsync
                // starts over with a fresh one.
                _refCount--;
                if (parent._connection == this)
                {
                    parent._connection = null;
                }

                throw;
            }
        }

        public AsyncDisposableValue<T>? Entry { get; private set; }
    }

    public sealed class Reference : IAsyncDisposableReference<T>
    {
        int _disposed;
        readonly Connection _connection;
        internal Reference(Connection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await _connection.DecrementRefCount();
        }

        public T Value => Volatile.Read(ref _disposed) == 1
            ? throw new ObjectDisposedException($"{nameof(RefCountTable<,>)}.{nameof(Reference)}")
            : _connection.Entry!.Value.Value;
    }
}