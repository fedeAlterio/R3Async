using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// A handle returned by the eager <c>Subscribe</c>-prefixed variants of aggregation operators (e.g.
/// <c>SubscribeFirstAsync</c>, <c>SubscribeCountAsync</c>). The subscription is already established by the time
/// this handle is returned, decoupling "subscribe" from "wait for the result" so callers can subscribe first,
/// trigger whatever produces the awaited value, and only then await <see cref="GetValueAsync(TimeSpan?, CancellationToken)"/>
/// - avoiding the race window inherent to operators that bundle subscribe-and-wait into a single await.
/// </summary>
/// <typeparam name="T">The type of the awaited result.</typeparam>
public readonly struct SubscriptionHandle<T>
{
    readonly Func<TimeSpan?, CancellationToken, ValueTask<T>> _waitResultAsync;
    readonly IAsyncDisposable _subscription;

    internal SubscriptionHandle(Func<TimeSpan?, CancellationToken, ValueTask<T>> waitResultAsync, IAsyncDisposable subscription)
    {
        _waitResultAsync = waitResultAsync;
        _subscription = subscription;
    }

    /// <summary>
    /// Waits for the result the subscription was set up to produce, optionally bounded by <paramref name="timeout"/>
    /// and/or <paramref name="cancellationToken"/>. Once the result is obtained (or the wait fails/times out), the
    /// underlying subscription is disposed.
    /// </summary>
    public ValueTask<T> GetValueAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _waitResultAsync?.Invoke(timeout, cancellationToken) ?? default;

    /// <summary>
    /// Waits for the result the subscription was set up to produce, bounded by <paramref name="cancellationToken"/>.
    /// Once the result is obtained (or the wait is canceled), the underlying subscription is disposed.
    /// </summary>
    public ValueTask<T> GetValueAsync(CancellationToken cancellationToken)
        => _waitResultAsync?.Invoke(null, cancellationToken) ?? default;

    /// <summary>Disposes the underlying subscription without waiting for a result.</summary>
    public ValueTask DisposeAsync() => _subscription?.DisposeAsync() ?? default;
}
