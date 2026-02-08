using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public interface IConnectionHub<in TKey, TValue> where TKey : notnull
{
    ValueTask<IConnection<TValue>> GetOrCreateConnectionAsync(TKey key, CancellationToken cancellationToken);
}

public interface IConnection<out T> : IAsyncDisposable
{
    T Value { get; }
}

public static class ConnectionHub
{
    public static ConnectionHub<TKey, TValue> Create<TKey, TValue>(Func<TKey, TValue> valueFactory) where TKey : notnull => new(valueFactory);
}

public class ConnectionHub<TKey, TValue>(Func<TKey, TValue> valueFactory) : IConnectionHub<TKey, TValue> where TKey : notnull
{
    readonly Func<TKey, TValue> _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
    readonly ConcurrentDictionary<TKey, Connection> _subjectsByKey = new();

    public ValueTask<IConnection<TValue>> GetOrCreateConnectionAsync(TKey key, CancellationToken cancellationToken)
    {
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subject = _subjectsByKey.GetOrAdd(key, static (key, args) =>
            {
                var (parent, valueFactory) = args;
                return new Connection(parent, key, new(() => valueFactory(key)));
            }, (this, _valueFactory));

            try
            {
                _ = subject.Value; // Force lazy evaluation
            }
            catch
            {
                _subjectsByKey.RemoveOnlyIfKeyValueMatch(key, subject);
                throw;
            }

            bool connectionDisposed;
            lock (subject.Gate)
            {
                subject.RefCount++;
                connectionDisposed = subject.ConnectionDisposed;
            }

            if (!connectionDisposed) return new(new SafeConnectionProxy(subject));
        } while (true);
    }

    sealed class Connection(ConnectionHub<TKey, TValue> parent, TKey key, Lazy<TValue> valueLazy) : IConnection<TValue>
    {
        public readonly object Gate = new();
        public int RefCount;
        public bool ConnectionDisposed;
        public ValueTask DisposeAsync()
        {
            lock (Gate)
            {
                if (--RefCount == 0)
                {
                    Volatile.Write(ref ConnectionDisposed, true);
                    parent._subjectsByKey.RemoveOnlyIfKeyValueMatch(key, this);
                }
            }

            return default;
        }

        public TValue Value => Volatile.Read(ref ConnectionDisposed)
            ? throw new ObjectDisposedException(nameof(Connection))
            : valueLazy.Value;
    }

    sealed class SafeConnectionProxy(IConnection<TValue> connection) : IConnection<TValue>
    {
        int _disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await connection.DisposeAsync();
        }

        public TValue Value => Volatile.Read(ref _disposed) == 1
            ? throw new ObjectDisposedException(nameof(SafeConnectionProxy))
            : connection.Value;
    }
}

file static class Ex
{
    public static bool RemoveOnlyIfKeyValueMatch<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value) where TKey : notnull
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(new KeyValuePair<TKey, TValue>(key, value));
    }
}