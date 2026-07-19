using System;

namespace R3Async;

/// <summary>
/// A type with a single value, used in place of <see cref="void"/> where a generic type parameter is required
/// (e.g. an <see cref="AsyncObservable{T}"/> that only signals occurrences with no meaningful payload). All
/// instances of <see cref="Unit"/> are equal to each other.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The single value of <see cref="Unit"/>.</summary>
    public static readonly Unit Default = default;

    /// <summary>A boxed instance of <see cref="Unit"/>, useful to avoid repeated boxing allocations.</summary>
    public static readonly object Box = default(Unit);

    /// <summary>Always returns <see langword="true"/>, since all <see cref="Unit"/> values are equal.</summary>
    public static bool operator ==(Unit first, Unit second)
    {
        return true;
    }

    /// <summary>Always returns <see langword="false"/>, since all <see cref="Unit"/> values are equal.</summary>
    public static bool operator !=(Unit first, Unit second)
    {
        return false;
    }

    /// <inheritdoc/>
    public bool Equals(Unit other)
    {
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is Unit;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return 0;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return "()";
    }
}