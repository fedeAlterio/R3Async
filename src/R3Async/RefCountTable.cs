using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static class RefCountTable
{
    public readonly record struct Entry<T>
    {
        public T Value { get; init; }
        public IAsyncDisposable Disposable { get; init; }
    }

    public static RefCountTable<TKey, TValue> Create<TKey, TValue>(Func<TKey, CancellationToken, Task<Entry<TValue>>> valueFactory) where TKey : notnull
    {
        return new RefCountTable<TKey, TValue>(valueFactory);
    }
}

public class RefCountTable<TKey, TValue>(Func<TKey, CancellationToken, Task<RefCountTable.Entry<TValue>>> valueFactory) where TKey : notnull
{
    readonly Func<TKey, CancellationToken, Task<RefCountTable.Entry<TValue>>> _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
    readonly ConcurrentDictionary<TKey, Connection> _subjectsByKey = new();

    public async ValueTask<Reference> GetOrCreateAsync(TKey key, CancellationToken cancellationToken)
    {
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = _subjectsByKey.GetOrAdd(key, static (key, args) =>
            {
                var (parent, valueFactory) = args;
                return new Connection(parent, key, valueFactory);
            }, (this, _valueFactory));

            try
            {
                await entry.EnsureValueExistsAsync(cancellationToken);
            }
            catch
            {
                _subjectsByKey.RemoveOnlyIfKeyValueMatch(key, entry);
                throw;
            }

            bool connectionDisposed;
            lock (entry.Gate)
            {
                entry.RefCount++;
                connectionDisposed = entry.ConnectionDisposed;
            }

            if (!connectionDisposed) return new Reference(entry);
        } while (true);
    }

    internal sealed class Connection(RefCountTable<TKey, TValue> parent, TKey key, Func<TKey, CancellationToken, Task<RefCountTable.Entry<TValue>>> valueFactory)
    
    {
        public readonly object Gate = new();
        TaskCompletionSource<object?>? _tcs;
        public int RefCount;
        public bool ConnectionDisposed;

        public ValueTask DisposeAsync()
        {
            bool shouldCleanup = false;
            lock (Gate)
            {
                if (--RefCount == 0)
                {
                    Volatile.Write(ref ConnectionDisposed, true);
                    parent._subjectsByKey.RemoveOnlyIfKeyValueMatch(key, this);
                    shouldCleanup = true;
                }
            }

            if (shouldCleanup)
            {
                return Entry.Disposable.DisposeAsync();
            }

            return default;
        }

        public async ValueTask EnsureValueExistsAsync(CancellationToken cancellationToken)
        {
            var tcs = Volatile.Read(ref _tcs);
            if (tcs is not null)
            {
                await tcs.Task;
                return;
            }

            var newTcs = new TaskCompletionSource<object?>();
            tcs = Interlocked.CompareExchange(ref _tcs, newTcs, null);
            if (tcs is not null)
            {
                await tcs.Task;
                return;
            }

            await CreateValueAsync(newTcs, cancellationToken);
        }

        public RefCountTable.Entry<TValue> Entry { get; set; }

        async ValueTask CreateValueAsync(TaskCompletionSource<object?> tcs, CancellationToken cancellationToken)
        {
            try
            {
                var value = await valueFactory(key, cancellationToken);
                Entry = value;
                tcs.TrySetResult(null);
            }
            catch (Exception e)
            {
                Volatile.Write(ref _tcs, null);
                tcs.TrySetException(e);
                throw;
            }
        }
    }

    public sealed class Reference
    {
        int _disposed;
        readonly Connection _connection;
        internal Reference(Connection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await _connection.DisposeAsync();
        }

        public TValue Value => Volatile.Read(ref _disposed) == 1
            ? throw new ObjectDisposedException($"{nameof(RefCountTable<,>)}.{nameof(Reference)}")
            : _connection.Entry.Value;
    }
}

file static class Ex
{
    public static bool RemoveOnlyIfKeyValueMatch<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value) where TKey : notnull
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(new KeyValuePair<TKey, TValue>(key, value));
    }
}