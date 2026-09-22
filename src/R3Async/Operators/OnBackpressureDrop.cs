using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Decouples a fast source from a slow observer: values are handed to a background drain loop through a
        /// single-slot channel that keeps only the most recent value. While the observer is processing, a newly
        /// pushed value replaces the pending one instead of queueing, so the source is never slowed down.
        /// </summary>
        public AsyncObservable<T> OnBackpressureDrop()
        {
            return CreateAsBackgroundJob<T>(async (observer, token) =>
            {
                var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

                await using var subscription = await @this.SubscribeAsync(
                    (x, _) =>
                    {
                        channel.Writer.TryWrite(x);
                        return default;
                    },
                    observer.OnErrorResumeAsync,
                    _ => { channel.Writer.TryComplete(); return default; },
                    token);

                await foreach (var value in channel.Reader.ReadAllAsync(token))
                {
                    await observer.OnNextAsync(value, token);
                }
            });
        }
    }
}
