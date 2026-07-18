using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using R3;

namespace R3Async.R3Interop;

public static class PublishingConfiguration
{
    public static PublishingConfiguration<T> Blocking<T>() => PublishingConfiguration<T>.BlockingInstance;

    public static PublishingConfiguration<T> NonBlocking<T>(Func<Channel<T>> channelFactory,
                                                            Action<T, ChannelWriter<T>> onNext,
                                                            Action<Exception, ChannelWriter<T>>? onErrorResume = null)
    {
        if (channelFactory is null)
            throw new ArgumentNullException(nameof(channelFactory));
        if (onNext is null)
            throw new ArgumentNullException(nameof(onNext));

        return new(PublishingConfiguration<T>.Kind.NonBlocking, channelFactory, onNext, onErrorResume);
    }
}

public sealed class PublishingConfiguration<T>
{
    internal enum Kind
    {
        Blocking,
        NonBlocking
    }

    internal PublishingConfiguration(Kind kind,
                                     Func<Channel<T>>? channelFactory,
                                     Action<T, ChannelWriter<T>>? onNext,
                                     Action<Exception, ChannelWriter<T>>? onErrorResume)
    {
        InternalKind = kind;
        ChannelFactory = channelFactory;
        OnNext = onNext;
        OnErrorResume = onErrorResume;
    }

    internal static PublishingConfiguration<T> BlockingInstance { get; } = new(Kind.Blocking, null, null, null);

    internal Kind InternalKind { get; }
    internal Func<Channel<T>>? ChannelFactory { get; }
    internal Action<T, ChannelWriter<T>>? OnNext { get; }
    internal Action<Exception, ChannelWriter<T>>? OnErrorResume { get; }
}

public static class R3ToAsyncObservableExtensions
{
    public static AsyncObservable<T> ToAsyncObservable<T>(this Observable<T> @this, PublishingConfiguration<T> publishingConfiguration)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (publishingConfiguration is null)
            throw new ArgumentNullException(nameof(publishingConfiguration));

        return publishingConfiguration.InternalKind switch
        {
            PublishingConfiguration<T>.Kind.Blocking => CreateBlocking(@this),
            _ => CreateNonBlocking(@this, publishingConfiguration)
        };
    }

    static AsyncObservable<T> CreateBlocking<T>(Observable<T> source)
    {
        return AsyncObservable.Create<T>((observer, cancellationToken) =>
        {
            var subscription = source.Subscribe(new BlockingObserver<T>(observer));
            return new ValueTask<IAsyncDisposable>(new SubscriptionAsyncDisposable(subscription));
        });
    }

    static AsyncObservable<T> CreateNonBlocking<T>(Observable<T> source, PublishingConfiguration<T> publishingConfiguration)
    {
        return AsyncObservable.CreateAsBackgroundJob<T>(async (observer, cancellationToken) =>
        {
            var channel = publishingConfiguration.ChannelFactory!();

            using var subscription = source.Subscribe(new ChannelObserver<T>(channel.Writer,
                                                                             publishingConfiguration.OnNext!,
                                                                             publishingConfiguration.OnErrorResume));

            try
            {
                while (await channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (channel.Reader.TryRead(out var value))
                    {
                        await observer.OnNextAsync(value, cancellationToken);
                    }
                }

                await observer.OnCompletedAsync(Result.Success);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                await observer.OnCompletedAsync(Result.Failure(e));
            }
        }, startSynchronously: true);
    }

    sealed class BlockingObserver<T>(AsyncObserver<T> observer) : Observer<T>
    {
        protected override void OnNextCore(T value) => WaitSynchronously(observer.OnNextAsync(value, CancellationToken.None));

        protected override void OnErrorResumeCore(Exception error) => WaitSynchronously(observer.OnErrorResumeAsync(error, CancellationToken.None));

        protected override void OnCompletedCore(R3.Result result) => WaitSynchronously(observer.OnCompletedAsync(ToAsyncResult(result)));

        static void WaitSynchronously(ValueTask task)
        {
            if (task.IsCompletedSuccessfully)
                return;

            task.AsTask().GetAwaiter().GetResult();
        }
    }

    sealed class SubscriptionAsyncDisposable(IDisposable subscription) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            subscription.Dispose();
            return default;
        }
    }

    sealed class ChannelObserver<T>(ChannelWriter<T> writer,
                                    Action<T, ChannelWriter<T>> onNext,
                                    Action<Exception, ChannelWriter<T>>? onErrorResume) : Observer<T>
    {
        protected override void OnNextCore(T value) => onNext(value, writer);

        protected override void OnErrorResumeCore(Exception error)
        {
            if (onErrorResume is null)
            {
                UnhandledExceptionHandler.OnUnhandledException(error);
                return;
            }

            onErrorResume(error, writer);
        }

        protected override void OnCompletedCore(R3.Result result) => writer.TryComplete(result.IsFailure ? result.Exception : null);
    }

    static Result ToAsyncResult(R3.Result result) => result.IsSuccess ? Result.Success : Result.Failure(result.Exception);
}
