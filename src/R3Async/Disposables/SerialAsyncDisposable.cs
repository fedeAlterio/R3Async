using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;
public class SerialDisposable : IAsyncDisposable
{
    IAsyncDisposable? _current;

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

        return default;
    }


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