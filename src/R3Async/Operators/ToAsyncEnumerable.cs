using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Converts the source into an <see cref="IAsyncEnumerable{T}"/> backed by a channel created via <paramref name="channelFactory"/>, which selects the
    /// backpressure semantics (rendezvous, bounded, or unbounded) between the emitting source and the consuming <c>await foreach</c> loop.
    /// </summary>
    /// <param name="channelFactory">Creates the channel used to bridge push-based source values into pull-based enumeration.</param>
    /// <param name="onErrorResume">Invoked for each resumable error from the source; if <see langword="null"/>, such errors are routed to the <see cref="UnhandledExceptionHandler"/>.</param>
    /// <remarks>
    /// The source is subscribed lazily, on the first <c>MoveNextAsync</c> call. Use <see cref="SubscribeToAsyncEnumerableAsync{T}"/> instead if you need the subscription
    /// to be active before enumeration starts (e.g. to avoid missing values emitted between deciding to subscribe and the first pull).
    /// </remarks>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this AsyncObservable<T> @this,
                                                           Func<Channel<T>> channelFactory,
                                                           Func<Exception, CancellationToken, ValueTask>? onErrorResume = null)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (channelFactory is null)
            throw new ArgumentNullException(nameof(channelFactory));

        return Impl(@this, channelFactory, onErrorResume);

        static async IAsyncEnumerable<T> Impl(AsyncObservable<T> @this,
                                              Func<Channel<T>> channelFactory,
                                              Func<Exception, CancellationToken, ValueTask>? onErrorResume,
                                              [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = channelFactory();
            await using var pipe = await @this.PipeAsync(channel.Writer, onErrorResume, cancellationToken);
            await foreach (var x in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return x;
            }
        }
    }

    // Unlike ToAsyncEnumerable, which subscribes lazily on first enumeration, this subscribes
    // eagerly (as part of the returned ValueTask) and hands back the still-open subscription
    // separately, so the caller can unsubscribe early without abandoning enumeration.
    /// <summary>
    /// Eagerly subscribes to the source and returns an <see cref="IAsyncDisposableReference{T}"/> whose <c>Value</c> is an <see cref="IAsyncEnumerable{T}"/>
    /// ready to enumerate immediately, backed by a channel created via <paramref name="channelFactory"/>.
    /// </summary>
    /// <param name="channelFactory">Creates the channel used to bridge push-based source values into pull-based enumeration.</param>
    /// <param name="onErrorResume">Invoked for each resumable error from the source; if <see langword="null"/>, such errors are routed to the <see cref="UnhandledExceptionHandler"/>.</param>
    /// <remarks>
    /// Unlike <see cref="ToAsyncEnumerable{T}"/>, which subscribes lazily on the first <c>MoveNextAsync</c>, this splits "subscribe" from "enumerate": by the time
    /// this await returns, the subscription is guaranteed active, eliminating the race window where values could be missed before enumeration begins.
    /// The returned reference's <c>DisposeAsync</c> unsubscribes independently of enumeration.
    /// </remarks>
    public static async ValueTask<IAsyncDisposableReference<IAsyncEnumerable<T>>> SubscribeToAsyncEnumerableAsync<T>(
        this AsyncObservable<T> @this,
        Func<Channel<T>> channelFactory,
        Func<Exception, CancellationToken, ValueTask>? onErrorResume = null,
        CancellationToken cancellationToken = default)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (channelFactory is null)
            throw new ArgumentNullException(nameof(channelFactory));

        var channel = channelFactory();
        var subscription = await @this.PipeAsync(channel.Writer, onErrorResume, cancellationToken);
        return new AsyncDisposableValue<IAsyncEnumerable<T>>
        {
            Value = channel.Reader.ReadAllAsync(CancellationToken.None),
            Disposable = subscription,
        };
    }
}
