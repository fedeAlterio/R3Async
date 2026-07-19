using System;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// A value paired with a disposable that releases resources associated with it. Disposing the reference disposes
/// the associated resource; the exposed <see cref="Value"/> itself is not necessarily what gets disposed (see
/// <see cref="AsyncDisposableValue{T}"/> for the case where it is).
/// </summary>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IAsyncDisposableReference<out T> : IAsyncDisposable
{
    /// <summary>The wrapped value.</summary>
    T Value { get; }
}

/// <summary>
/// An <see cref="IAsyncDisposableReference{T}"/> pairing a value with the <see cref="IAsyncDisposable"/> that
/// releases it, allowing the disposal logic to differ from the value's own type.
/// </summary>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public readonly struct AsyncDisposableValue<T> : IAsyncDisposableReference<T>
{
    /// <summary>The wrapped value.</summary>
    public required T Value { get; init; }

    /// <summary>The disposable invoked when this instance is disposed.</summary>
    public required IAsyncDisposable Disposable { get; init; }

    /// <summary>Disposes <see cref="Disposable"/>.</summary>
    public ValueTask DisposeAsync() => Disposable.DisposeAsync();
}

/// <summary>Factory helpers for <see cref="AsyncDisposableValue{T}"/>.</summary>
public static class AsyncDisposableValue
{
    /// <summary>
    /// Wraps <paramref name="value"/> in an <see cref="AsyncDisposableValue{T}"/> that uses the value itself as
    /// its own <see cref="AsyncDisposableValue{T}.Disposable"/>.
    /// </summary>
    public static AsyncDisposableValue<T> From<T>(T value) where T : IAsyncDisposable
    {
        return new()
        {
            Value = value,
            Disposable = value
        };
    }
}