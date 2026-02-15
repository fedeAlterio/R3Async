using System;

namespace R3Async;

public interface IAsyncDisposableReference<out T> : IAsyncDisposable
{
    T Value { get; }
}

public readonly struct AsyncDisposableValue<T>
{
    public required T Value { get; init; }
    public required IAsyncDisposable Disposable { get; init; }
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