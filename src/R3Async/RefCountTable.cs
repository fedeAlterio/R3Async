using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static class RefCountTable
{
    public static RefCountTable<TKey, TValue> Create<TKey, TValue>(Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> valueFactory) where TKey : notnull
    {
        return new RefCountTable<TKey, TValue>(valueFactory);
    }
}

public class RefCountTable<TKey, TValue>(Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> valueFactory) where TKey : notnull
{
    readonly Func<TKey, CancellationToken, Task<AsyncDisposableValue<TValue>>> _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
    readonly ConcurrentDictionary<TKey, Connection> _subjectsByKey = new();

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
                    _connectionDisposed = true;
                    parent._subjectsByKey.RemoveOnlyIfKeyValueMatch(key, this);
                    await Entry!.Value.Disposable.DisposeAsync();
                }
            }
        }

        public async ValueTask<bool> IncrementRefCount(CancellationToken cancellationToken)
        {
            using (await Gate.LockAsync())
            {
                if (_connectionDisposed) return true;

                _refCount++;
                Entry ??= await valueFactory(key, cancellationToken);
                return false;
            }
        }

        public AsyncDisposableValue<TValue>? Entry { get; private set; }
    }

    public sealed class Reference : IAsyncDisposableReference<TValue>
    {
        int _disposed;
        readonly Connection _connection;
        internal Reference(Connection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await _connection.DecrementRefCount();
        }

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