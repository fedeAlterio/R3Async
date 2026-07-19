using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// Holds a disposable that can be assigned exactly once via <see cref="SetDisposableAsync"/>; assigning a second
/// time throws. Useful for wiring up a resource whose creation completes asynchronously after subscription setup
/// has already returned a disposable handle to the caller.
/// </summary>
public sealed class SingleAssignmentAsyncDisposable : IAsyncDisposable
{
    IAsyncDisposable? _current;

    /// <summary>Gets whether this instance has been disposed.</summary>
    public bool IsDisposed => ReferenceEquals(Volatile.Read(ref _current), DisposedSentinel.Instance);

    /// <summary>
    /// Gets the currently assigned disposable, or <see langword="null"/> if none has been assigned yet. Returns
    /// <see cref="R3Async.AsyncDisposable.Empty"/> if this instance has been disposed.
    /// </summary>
    public IAsyncDisposable? GetDisposable()
    {
        var field = Volatile.Read(ref _current);
        if (ReferenceEquals(field, DisposedSentinel.Instance))
        {
            return AsyncDisposable.Empty;
        }

        return field;
    }

    internal static ValueTask SetDisposableAsync(ref IAsyncDisposable? field, IAsyncDisposable? value)
    {
        var current = Interlocked.CompareExchange(ref field, value, null);
        if (current == null)
        {
            // ok to set.
            return default;
        }

        if (ReferenceEquals(current, DisposedSentinel.Instance))
        {
            if (value is not null)
            {
                return value.DisposeAsync();
            }

            return default;
        }

        // otherwise, invalid assignment
        ThrowAlreadyAssignment();
        return default;
    }

    /// <summary>
    /// Assigns <paramref name="value"/> as the disposable held by this instance. If this instance has already
    /// been disposed, <paramref name="value"/> is disposed immediately instead of being stored.
    /// </summary>
    /// <exception cref="InvalidOperationException">A disposable has already been assigned.</exception>
    public ValueTask SetDisposableAsync(IAsyncDisposable? value) => SetDisposableAsync(ref _current, value);

    [DebuggerStepThrough]
    internal static ValueTask DisposeAsync(ref IAsyncDisposable? field)
    {
        var current = Interlocked.Exchange(ref field, DisposedSentinel.Instance);
        if (!ReferenceEquals(current, DisposedSentinel.Instance) && current is not null)
        {
            return current.DisposeAsync();
        }

        return default;
    }

    /// <summary>
    /// Disposes the assigned disposable (if any) and marks this instance as disposed. Safe to call multiple times;
    /// subsequent calls are no-ops. Any disposable subsequently passed to <see cref="SetDisposableAsync"/> will be
    /// disposed immediately.
    /// </summary>
    public ValueTask DisposeAsync() => DisposeAsync(ref _current);

    static void ThrowAlreadyAssignment()
    {
        throw new InvalidOperationException("Disposable is already assigned.");
    }

    sealed class DisposedSentinel : IAsyncDisposable
    {
        public static readonly DisposedSentinel Instance = new();
        public ValueTask DisposeAsync() => default;
    }
}
