using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// An <see cref="AsyncObservable{T}"/> that multicasts a single subscription to its underlying source among all
/// of its subscribers. Subscribers do not receive any values until <see cref="ConnectAsync"/> is called; produced
/// by operators such as <c>Multicast</c> and <c>Publish</c>.
/// </summary>
/// <typeparam name="T">The type of the values produced by the stream.</typeparam>
public abstract class ConnectableAsyncObservable<T> : AsyncObservable<T>
{
    /// <summary>
    /// Connects to the underlying source, starting to multicast its values to all current and future subscribers.
    /// Disposing the returned <see cref="IAsyncDisposable"/> disconnects from the source.
    /// </summary>
    public abstract ValueTask<IAsyncDisposable> ConnectAsync(CancellationToken cancellationToken);
}
