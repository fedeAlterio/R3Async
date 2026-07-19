using R3Async.Subjects;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>Subscribes to <paramref name="@this"/> and forwards every notification (values, resumable errors, completion) directly into <paramref name="subject"/>.</summary>
        /// <param name="subject">The subject that receives all notifications from <paramref name="@this"/>.</param>
        public ValueTask<IAsyncDisposable> PipeAsync(ISubject<T> subject)
        {
            return @this.SubscribeAsync(subject.OnNextAsync, subject.OnErrorResumeAsync, subject.OnCompletedAsync);
        }

        /// <summary>
        /// Subscribes to <paramref name="@this"/> and writes every value into <paramref name="channelWriter"/>,
        /// completing the channel writer (successfully or with the failure exception) when the source completes.
        /// </summary>
        /// <param name="channelWriter">The channel writer that receives values from <paramref name="@this"/> and is completed when the source completes.</param>
        /// <param name="onErrorResume">
        /// Optional handler for resumable errors from <paramref name="@this"/>. Defaults to completing
        /// <paramref name="channelWriter"/> with the error, terminating the channel.
        /// </param>
        /// <param name="cancellationToken">The token used for the subscription operation.</param>
        public ValueTask<IAsyncDisposable> PipeAsync(ChannelWriter<T> channelWriter,
                                                     Func<Exception, CancellationToken, ValueTask>? onErrorResume = null,
                                                     CancellationToken cancellationToken = default)
        {
            var onErrorResumeAsync = onErrorResume ?? ((e, _) =>
            {
                channelWriter.TryComplete(e);
                return default;
            });

            return @this.SubscribeAsync(channelWriter.WriteAsync, onErrorResumeAsync, result =>
            {
                channelWriter.TryComplete(result.Exception);
                return default;
            }, cancellationToken);
        }
    }
}
