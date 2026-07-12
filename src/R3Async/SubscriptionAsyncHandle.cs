using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public readonly struct SubscriptionHandle<T>
{
    readonly Func<TimeSpan?, CancellationToken, ValueTask<T>> _waitResultAsync;
    readonly IAsyncDisposable _subscription;

    internal SubscriptionHandle(Func<TimeSpan?, CancellationToken, ValueTask<T>> waitResultAsync, IAsyncDisposable subscription)
    {
        _waitResultAsync = waitResultAsync;
        _subscription = subscription;
    }

    public ValueTask<T> GetValueAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _waitResultAsync?.Invoke(timeout, cancellationToken) ?? default;

    public ValueTask<T> GetValueAsync(CancellationToken cancellationToken)
        => _waitResultAsync?.Invoke(null, cancellationToken) ?? default;

    public ValueTask DisposeAsync() => _subscription?.DisposeAsync() ?? default;
}
