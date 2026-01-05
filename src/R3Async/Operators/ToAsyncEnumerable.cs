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
            var onErrorResumeAsync = onErrorResume ?? ((e, _) =>
            {
                channel.Writer.Complete(e);
                return default;
            });

            await using var subscription = await @this.SubscribeAsync(channel.Writer.WriteAsync, onErrorResumeAsync, result =>
            {
                channel.Writer.Complete(result.Exception);
                return default;
            }, cancellationToken);

            await foreach (var x in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return x;
            }
        }
    }
}
