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
        public ValueTask<IAsyncDisposable> PipeAsync(ISubject<T> subject)
        {
            return @this.SubscribeAsync(subject.OnNextAsync, subject.OnErrorResumeAsync, subject.OnCompletedAsync);
        }

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
