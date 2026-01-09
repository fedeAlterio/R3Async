using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public sealed class SingleAssignmentAsyncDisposable : IAsyncDisposable
{
    IAsyncDisposable? _current;

    public bool IsDisposed => ReferenceEquals(Volatile.Read(ref _current), DisposedSentinel.Instance);

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

    public ValueTask SetDisposableAsync(IAsyncDisposable? value) => SetDisposableAsync(ref _current, value);

    internal static ValueTask DisposeAsync(ref IAsyncDisposable? field)
    {
        var current = Interlocked.Exchange(ref field, DisposedSentinel.Instance);
        if (!ReferenceEquals(current, DisposedSentinel.Instance) && current is not null)
        {
            return current.DisposeAsync();
        }

        return default;
    }

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
