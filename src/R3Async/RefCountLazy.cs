using R3Async.Internals;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// A reference-counted lazily-created resource: the first call to <see cref="GetAsync"/> creates the value using
/// <paramref name="valueFactory"/>, subsequent calls share it, and it is disposed once every returned reference
/// has been disposed. A later call to <see cref="GetAsync"/> after the resource has been fully released creates a
/// brand-new instance via <paramref name="valueFactory"/>. Thread-safe for concurrent access.
/// </summary>
/// <typeparam name="T">The type of the shared resource.</typeparam>
/// <param name="valueFactory">Creates the resource (and its disposal callback) the first time it is requested.</param>
public class RefCountLazy<T>(Func<CancellationToken, ValueTask<AsyncDisposableValue<T>>> valueFactory)
{
    readonly AsyncGate _gate = new();

    Connection? _connection;

    /// <summary>
    /// Gets a reference to the shared resource, creating it via the value factory if this is the first outstanding
    /// reference. Dispose the returned reference to release it; the resource itself is disposed once all
    /// references have been disposed.
    /// </summary>
    /// <param name="cancellationToken">Used only while creating the resource, if it does not already exist.</param>
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

    /// <summary>A reference to the resource shared by a <see cref="RefCountLazy{T}"/>, returned by <see cref="GetAsync"/>.</summary>
    public sealed class Reference : IAsyncDisposableReference<T>
    {
        int _disposed;
        readonly Connection _connection;
        internal Reference(Connection connection) => _connection = connection;

        /// <summary>Releases this reference. Once every outstanding reference has been disposed, the underlying resource is disposed as well.</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await _connection.DecrementRefCount();
        }

        /// <summary>The shared resource value. Throws <see cref="ObjectDisposedException"/> if this reference has already been disposed.</summary>
        public T Value => Volatile.Read(ref _disposed) == 1
            ? throw new ObjectDisposedException($"{nameof(RefCountTable<,>)}.{nameof(Reference)}")
            : _connection.Entry!.Value.Value;
    }
}