using System;
using System.Threading.Tasks;

namespace R3Async.Helpers;

public static class AsyncDisposableEx
{
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
