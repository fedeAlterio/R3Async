using System;
using System.Threading.Tasks;

namespace R3Async.Helpers;

/// <summary>Extension helpers for adapting synchronous <see cref="IDisposable"/> instances to the async world.</summary>
public static class AsyncDisposableEx
{
    /// <summary>
    /// Wraps a synchronous <see cref="IDisposable"/> so it can be used as an <see cref="IAsyncDisposable"/>;
    /// disposal simply calls the synchronous <see cref="IDisposable.Dispose"/> and completes synchronously.
    /// </summary>
    public static IAsyncDisposable ToAsyncDisposable(this IDisposable @this)
    {
        return new DisposableToAsyncDisposable(@this);
    }

    sealed class DisposableToAsyncDisposable(IDisposable disposable) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposable.Dispose();
            return default;
        }
    }
}
