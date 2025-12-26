using System;
using System.Threading.Tasks;

namespace R3Async;

public static class AsyncDisposable
{
    public static IAsyncDisposable Create(Func<ValueTask> disposeAsync) => new AnonymousAsyncDisposable(disposeAsync);
    public static IAsyncDisposable Empty { get; } = new EmptyAsyncDisposable();
    sealed class AnonymousAsyncDisposable(Func<ValueTask> disposeAsync) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => disposeAsync();
    }

    sealed class EmptyAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new ();
    }
}
