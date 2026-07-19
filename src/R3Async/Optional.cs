using System;
using System.Runtime.CompilerServices;

namespace R3Async;

/// <summary>
/// A value that may or may not be present, distinguishing "no value" from a value that happens to be
/// <see langword="null"/> or <c>default</c>.
/// </summary>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public readonly struct Optional<T>
{
    readonly T? _value;

    /// <summary>Creates an empty <see cref="Optional{T}"/> with no value, equivalent to <see cref="Empty"/>.</summary>
    public Optional() => (_value, HasValue) = (default, false);

    /// <summary>Creates an <see cref="Optional{T}"/> wrapping <paramref name="value"/>.</summary>
    public Optional(T value) => (_value, HasValue) = (value, true);

    /// <summary>Gets an empty <see cref="Optional{T}"/> with no value.</summary>
    public static Optional<T> Empty => new();

    /// <summary>Gets whether this instance holds a value.</summary>
    public bool HasValue { get; }

    /// <summary>Gets the wrapped value.</summary>
    /// <exception cref="InvalidOperationException"><see cref="HasValue"/> is <see langword="false"/>.</exception>
    public T? Value => HasValue ? _value : throw new InvalidOperationException("Impossible retrieve a value for an empty optional");
}

/// <summary>Extension helpers for <see cref="Optional{T}"/>.</summary>
public static class OptionalExtensions
{
    /// <summary>
    /// Attempts to retrieve the wrapped value without throwing. Returns <see langword="true"/> and sets
    /// <paramref name="value"/> when <see cref="Optional{T}.HasValue"/> is <see langword="true"/>; otherwise
    /// returns <see langword="false"/> and sets <paramref name="value"/> to <c>default</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValue<T>(this Optional<T> @this, out T value)
    {
        var hasValue = @this.HasValue;
        value = hasValue ? @this.Value! : default!;
        return hasValue;
    }
}