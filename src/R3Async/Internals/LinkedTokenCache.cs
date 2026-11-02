using System;
using System.Threading;

namespace R3Async.Internals;

// Reuses the linked source while the caller token stays the same; a replaced source is disposed once no lease uses it.
internal sealed class LinkedTokenCache
{
    readonly object _gate = new();
    readonly CancellationToken _disposeToken;
    CancellationToken _callerToken;
    Entry? _current;
    bool _disposed;

    public LinkedTokenCache(CancellationToken disposeToken)
    {
        _disposeToken = disposeToken;
        disposeToken.Register(static state => ((LinkedTokenCache)state!).Dispose(), this);
    }

    public Lease Rent(CancellationToken callerToken)
    {
        if (!callerToken.CanBeCanceled)
        {
            return new(null, null, _disposeToken);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return new(null, null, _disposeToken);
            }

            if (_current is null || _callerToken != callerToken)
            {
                Retire(_current);
                _current = new(CancellationTokenSource.CreateLinkedTokenSource(callerToken, _disposeToken));
                _callerToken = callerToken;
            }

            _current.Leases++;
            return new(this, _current, _current.Token);
        }
    }

    void Return(Entry entry)
    {
        lock (_gate)
        {
            if (--entry.Leases == 0 && entry.Retired)
            {
                entry.Source.Dispose();
            }
        }
    }

    void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Retire(_current);
            _current = null;
            _callerToken = default;
        }
    }

    static void Retire(Entry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.Retired = true;
        if (entry.Leases == 0)
        {
            entry.Source.Dispose();
        }
    }

    internal sealed class Entry(CancellationTokenSource source)
    {
        public readonly CancellationTokenSource Source = source;
        public readonly CancellationToken Token = source.Token;
        public int Leases;
        public bool Retired;
    }

    public readonly struct Lease : IDisposable
    {
        readonly LinkedTokenCache? _cache;
        readonly Entry? _entry;

        internal Lease(LinkedTokenCache? cache, Entry? entry, CancellationToken token)
        {
            _cache = cache;
            _entry = entry;
            Token = token;
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            if (_entry is not null)
            {
                _cache!.Return(_entry);
            }
        }
    }
}
