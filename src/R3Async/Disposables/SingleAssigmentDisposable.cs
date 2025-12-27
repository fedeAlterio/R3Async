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

    public async ValueTask SetDisposableAsync(IAsyncDisposable? value)
    {
        var field = Interlocked.CompareExchange(ref _current, value, null);
        if (field == null)
        {
            // ok to set.
            return;
        }

        if (ReferenceEquals(field, DisposedSentinel.Instance))
        {
            if (value is not null)
            {
                await value.DisposeAsync();
            }

            return;
        }

        // otherwise, invalid assignment
        ThrowAlreadyAssignment();
    }

    public async ValueTask DisposeAsync()
    {
        var field = Interlocked.Exchange(ref _current, DisposedSentinel.Instance);
        if (!ReferenceEquals(field, DisposedSentinel.Instance) && field is not null)
        {
            await field.DisposeAsync();
        }
    }

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
