using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public class RefCountValue<T>(Func<CancellationToken, ValueTask<AsyncDisposableValue<T>>> valueFactory)
{
    int _refCount;
    AsyncDisposableValue<T> _entry;
    TaskCompletionSource<object?>? _tcs;

    public async ValueTask<IAsyncDisposableReference<T>> GetAsync(CancellationToken cancellationToken)
    {
        await EnsureValueExistsAsync(cancellationToken);
        Interlocked.Increment(ref _refCount);
        
        return new Reference(this);
    }

    async ValueTask EnsureValueExistsAsync(CancellationToken cancellationToken)
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

    async ValueTask CreateValueAsync(TaskCompletionSource<object?> tcs, CancellationToken cancellationToken)
    {
        try
        {
            var value = await valueFactory(cancellationToken);
            _entry = value;
            tcs.TrySetResult(null);
        }
        catch (Exception e)
        {
            Volatile.Write(ref _tcs, null);
            tcs.TrySetException(e);
            throw;
        }
    }

    async ValueTask DisposeReferenceAsync()
    {
        if(Interlocked.Decrement(ref _refCount) == 0)
        {
            await _entry.Disposable.DisposeAsync();
        }
    }

    public sealed class Reference : IAsyncDisposableReference<T>
    {
        int _disposed;
        readonly RefCountValue<T> _parent;

        internal Reference(RefCountValue<T> parent) => _parent = parent;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            await _parent.DisposeReferenceAsync();
        }

        public T Value => Volatile.Read(ref _disposed) == 1
            ? throw new ObjectDisposedException($"{nameof(RefCountValue<T>)}.{nameof(Reference)}")
            : _parent._entry.Value;
    }
}