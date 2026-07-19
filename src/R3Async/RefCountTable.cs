using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

/// <summary>Non-generic factory for <see cref="RefCountTable{TKey, TValue}"/>.</summary>
public static class RefCountTable
{
    /// <summary>Creates a <see cref="RefCountTable{TKey, TValue}"/> that lazily creates a value per key using <paramref name="valueFactory"/> and shares it among all callers requesting that key.</summary>
    /// <typeparam name="TKey">The type of the keys identifying each shared resource.</typeparam>
    /// <typeparam name="TValue">The type of the shared resource.</typeparam>
    /// <param name="valueFactory">Creates the resource (and its disposal callback) for a key the first time it is requested.</param>
    public static RefCountTable<TKey, TValue> Create<TKey, TValue>(Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> valueFactory) where TKey : notnull
    {
        return new RefCountTable<TKey, TValue>(valueFactory);
    }
}

/// <summary>
/// A dictionary of reference-counted resources keyed by <typeparamref name="TKey"/>, acting as a message hub or
/// resource registry: resources are created on demand via the value factory and automatically disposed once every
/// reference to a given key has been released, preventing leaks. Operations are idempotent per key - whichever
/// caller (consumer or producer) requests a key first creates the resource, and everyone after shares it. Thread-safe for concurrent access.
/// </summary>
/// <typeparam name="TKey">The type of the keys identifying each shared resource.</typeparam>
/// <typeparam name="TValue">The type of the shared resource.</typeparam>
/// <param name="valueFactory">Creates the resource (and its disposal callback) for a key the first time it is requested.</param>
public class RefCountTable<TKey, TValue>(Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> valueFactory) where TKey : notnull
{
    readonly Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
    readonly ConcurrentDictionary<TKey, Connection> _subjectsByKey = new();

    /// <summary>
    /// Gets a reference to the resource for <paramref name="key"/>, creating it via the value factory if this is
    /// the first outstanding reference for that key. Dispose the returned reference to release it; the resource
    /// itself is disposed once every reference for that key has been disposed, after which a new request for the
    /// same key creates a fresh resource.
    /// </summary>
    /// <param name="key">The key identifying the shared resource.</param>
    /// <param name="cancellationToken">Used only while creating the resource for this key, if it does not already exist.</param>
    public async ValueTask<IAsyncDisposableReference<TValue>> GetOrCreateAsync(TKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            var entry = _subjectsByKey.GetOrAdd(key, static (key, args) =>
            {
                var (parent, valueFactory) = args;
                return new Connection(parent, key, valueFactory);
            }, (this, _valueFactory));

            var isDisposed = await entry.IncrementRefCount(cancellationToken);
            if (!isDisposed) return new Reference(entry);
        } 
    }

    internal sealed class Connection(RefCountTable<TKey, TValue> parent, TKey key, Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> valueFactory)

    {
        public readonly AsyncGate Gate = new();
        int _refCount;
        bool _connectionDisposed;

        public async ValueTask DecrementRefCount()
        {
            using (await Gate.LockAsync())
            {
                if (--_refCount == 0)
                {
                    SetDisposedAndDeleteFromDictionary();
                    await Entry!.Value.Disposable.DisposeAsync();
                }
            }
        }

        void SetDisposedAndDeleteFromDictionary()
        {
            _connectionDisposed = true;
            parent._subjectsByKey.RemoveOnlyIfKeyValueMatch(key, this);
        }

        public async ValueTask<bool> IncrementRefCount(CancellationToken cancellationToken)
        {
            using (await Gate.LockAsync())
            {
                if (_connectionDisposed) return true;

                _refCount++;
                if (Entry is not null) return false;

                try
                {
                    Entry = await valueFactory(key, cancellationToken);
                    return false;
                }
                catch
                {
                    // The factory failed, so no Reference is handed out for this increment and the
                    // count can never reach zero again. Kill the whole connection: pending and future
                    // callers see it as disposed and retry with a fresh one.
                    SetDisposedAndDeleteFromDictionary();
                    throw;
                }
            }
        }

        public AsyncDisposableValue<TValue>? Entry { get; private set; }
    }

    /// <summary>A reference to a keyed resource shared by a <see cref="RefCountTable{TKey, TValue}"/>, returned by <see cref="GetOrCreateAsync"/>.</summary>
    public sealed class Reference : IAsyncDisposableReference<TValue>
    {
        int _disposed;
        readonly Connection _connection;
        internal Reference(Connection connection) => _connection = connection;

        /// <summary>Releases this reference. Once every outstanding reference for the same key has been disposed, the underlying resource is disposed as well.</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await _connection.DecrementRefCount();
        }

        /// <summary>The shared resource value. Throws <see cref="ObjectDisposedException"/> if this reference has already been disposed.</summary>
        public TValue Value => Volatile.Read(ref _disposed) == 1
            ? throw new ObjectDisposedException($"{nameof(RefCountTable<,>)}.{nameof(Reference)}")
            : _connection.Entry!.Value.Value;
    }
}

file static class Ex
{
    public static bool RemoveOnlyIfKeyValueMatch<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value) where TKey : notnull
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(new KeyValuePair<TKey, TValue>(key, value));
    }
}