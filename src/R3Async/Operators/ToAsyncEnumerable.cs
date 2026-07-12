using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
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
            Value = channel.Reader.ReadAllAsync(cancellationToken),
            Disposable = subscription,
        };
    }
}
