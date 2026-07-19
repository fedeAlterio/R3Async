using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>Factory helpers for creating <see cref="IAsyncDisposable"/> instances from delegates.</summary>
public static class AsyncDisposable
{
    /// <summary>
    /// Creates an <see cref="IAsyncDisposable"/> that invokes <paramref name="disposeAsync"/> on the first call to
    /// <see cref="IAsyncDisposable.DisposeAsync"/>. Subsequent calls are no-ops.
    /// </summary>
    public static IAsyncDisposable Create(Func<ValueTask> disposeAsync) => new AnonymousAsyncDisposable(disposeAsync);

    /// <summary>
    /// Creates an <see cref="IAsyncDisposable"/> that invokes the synchronous <paramref name="dispose"/> action on
    /// the first call to <see cref="IAsyncDisposable.DisposeAsync"/>. Subsequent calls are no-ops.
    /// </summary>
    public static IAsyncDisposable Create(Action dispose) => new AnonymousAsyncSyncDisposable(dispose);

    /// <summary>Gets a shared <see cref="IAsyncDisposable"/> instance whose <see cref="IAsyncDisposable.DisposeAsync"/> does nothing.</summary>
    public static IAsyncDisposable Empty { get; } = new EmptyAsyncDisposable();
    sealed class AnonymousAsyncDisposable(Func<ValueTask> disposeAsync) : IAsyncDisposable
    {
        int _disposed;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 1 ? default : disposeAsync();
    }

    sealed class AnonymousAsyncSyncDisposable(Action dispose) : IAsyncDisposable
    {
        int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return default;
            dispose();
            return default;
        }
    }


    sealed class EmptyAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new ();
    }
}
