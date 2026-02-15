using System;
using System.Threading.Tasks;

namespace R3Async;

public interface IAsyncDisposableReference<out T> : IAsyncDisposable
{
    T Value { get; }
}

public readonly struct AsyncDisposableValue<T> : IAsyncDisposableReference<T>
{
    public required T Value { get; init; }
    public required IAsyncDisposable Disposable { get; init; }
    public ValueTask DisposeAsync() => Disposable.DisposeAsync();
}

public static class AsyncDisposableValue
{
    public static AsyncDisposableValue<T> From<T>(T value) where T : IAsyncDisposable
    {
        return new()
        {
            Value = value,
            Disposable = value
        };
    }
}