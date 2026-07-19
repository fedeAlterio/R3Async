using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;
/// <summary>
/// Holds a single "current" <see cref="IAsyncDisposable"/> that can be replaced at any time; assigning a new value
/// disposes the previous one. Useful for a resource that gets swapped out over time (e.g. the active source
/// subscription in a `Switch`-like operator).
/// </summary>
public class SerialAsyncDisposable : IAsyncDisposable
{
    IAsyncDisposable? _current;

    /// <summary>
    /// Replaces the current disposable with <paramref name="value"/>, disposing the previous one (if any). If this
    /// instance has already been disposed, <paramref name="value"/> is disposed immediately instead of being stored.
    /// </summary>
    public ValueTask SetDisposableAsync(IAsyncDisposable? value)
    {
        var field = Volatile.Read(ref _current);
        while (true)
        {
            if (ReferenceEquals(field, DisposedSentinel.Instance))
            {
                // We've already been disposed, so dispose the value we've just been given.
                if (value is not null)
                {
                    return value.DisposeAsync();
                }

                return default;
            }

            var exchangedCurrent = Interlocked.CompareExchange(ref _current, value, field);
            if (ReferenceEquals(exchangedCurrent, field))
            {
                if (exchangedCurrent is not null)
                {
                    return exchangedCurrent.DisposeAsync();
                }

                return default;
            }

            field = exchangedCurrent;
        }
    }


    /// <summary>
    /// Disposes the current disposable (if any) and marks this instance as disposed. Any disposable subsequently
    /// passed to <see cref="SetDisposableAsync"/> will be disposed immediately instead of being stored. Safe to
    /// call multiple times; subsequent calls are no-ops.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        var field = Interlocked.Exchange(ref _current, DisposedSentinel.Instance);
        if (!ReferenceEquals(field, DisposedSentinel.Instance))
        {
            if (field is not null)
            {
                return field.DisposeAsync();
            }
        }

        return default;
    }

    sealed class DisposedSentinel : IAsyncDisposable
    {
        public static readonly DisposedSentinel Instance = new();
        public ValueTask DisposeAsync() => default;
    }
}