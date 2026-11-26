using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using R3Async.Internals;

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
            return Create<T>(async (observer, cancellationToken) =>
            {
                var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

                var subscription = await @this.SubscribeAsync(
                    (x, _) =>
                    {
                        channel.Writer.TryWrite(x);
                        return default;
                    },
                    (e, _) => { channel.Writer.TryComplete(e); return default; },
                    r => { channel.Writer.TryComplete(r.Exception); return default; },
                    cancellationToken);

                var drain = CancelableTaskSubscription.CreateAndStart<T>(async (obs, token) =>
                {
                    await foreach (var value in channel.Reader.ReadAllAsync(token))
                    {
                        await obs.OnNextAsync(value, token);
                    }

                    await obs.OnCompletedAsync(Result.Success);
                }, observer);

                return AsyncDisposable.Create(async () =>
                {
                    await drain.DisposeAsync();
                    await subscription.DisposeAsync();
                });
            });
        }
    }
}
