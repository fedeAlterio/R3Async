using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static class AsyncDisposable
{
    public static IAsyncDisposable Create(Func<ValueTask> disposeAsync) => new AnonymousAsyncDisposable(disposeAsync);
    public static IAsyncDisposable Create(Action dispose) => new AnonymousAsyncSyncDisposable(dispose);
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
