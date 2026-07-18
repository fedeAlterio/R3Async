using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using R3;

namespace R3Async.R3Interop;

public static class BackpressureStrategy
{
    public static BlockingBackpressureStrategy Blocking { get; } = new();

    public static UnboundedChannelBackpressureStrategy FromUnboundedChannel(UnboundedChannelOptions? options = null)
    {
        return new(options);
    }

    public static ChannelBackpressureStrategy<T> FromUnboundedChannel<T>(Action<Exception, ChannelWriter<T>> onErrorResume,
                                                                         UnboundedChannelOptions? options = null)
    {
        if (onErrorResume is null)
            throw new ArgumentNullException(nameof(onErrorResume));

        return UnboundedChannelBackpressureStrategy.ToChannelStrategy(options, onErrorResume);
    }

    public static BoundedChannelBackpressureStrategy FromBoundedChannel(int capacity)
    {
        return new(new BoundedChannelOptions(capacity));
    }

    public static BoundedChannelBackpressureStrategy FromBoundedChannel(BoundedChannelOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        return new(options);
    }

    public static ChannelBackpressureStrategy<T> FromBoundedChannel<T>(Action<Exception, ChannelWriter<T>> onErrorResume, int capacity)
    {
        return FromBoundedChannel(onErrorResume, new BoundedChannelOptions(capacity));
    }

    public static ChannelBackpressureStrategy<T> FromBoundedChannel<T>(Action<Exception, ChannelWriter<T>> onErrorResume, BoundedChannelOptions options)
    {
        if (onErrorResume is null)
            throw new ArgumentNullException(nameof(onErrorResume));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        return BoundedChannelBackpressureStrategy.ToChannelStrategy(options, onErrorResume);
    }

    public static ChannelBackpressureStrategy<T> FromChannel<T>(Func<Channel<T>> channelFactory,
                                                                Action<T, ChannelWriter<T>>? onNext = null,
                                                                Action<Exception, ChannelWriter<T>>? onErrorResume = null)
    {
        if (channelFactory is null)
            throw new ArgumentNullException(nameof(channelFactory));

        return new(channelFactory, onNext ?? (static (x, c) => c.TryWrite(x)), onErrorResume);
    }
}

public sealed class BlockingBackpressureStrategy
{
    internal BlockingBackpressureStrategy()
    {
    }
}

public sealed class UnboundedChannelBackpressureStrategy
{
    readonly UnboundedChannelOptions? options;

    internal UnboundedChannelBackpressureStrategy(UnboundedChannelOptions? options)
    {
        this.options = options;
    }

    internal ChannelBackpressureStrategy<T> ToChannelStrategy<T>() => ToChannelStrategy<T>(options, null);

    internal static ChannelBackpressureStrategy<T> ToChannelStrategy<T>(UnboundedChannelOptions? options,
                                                                        Action<Exception, ChannelWriter<T>>? onErrorResume)
    {
        return BackpressureStrategy.FromChannel(() => options is null ? Channel.CreateUnbounded<T>() : Channel.CreateUnbounded<T>(options),
                                                static (x, c) => c.TryWrite(x),
                                                onErrorResume);
    }
}

public sealed class BoundedChannelBackpressureStrategy
{
    readonly BoundedChannelOptions options;

    internal BoundedChannelBackpressureStrategy(BoundedChannelOptions options)
    {
        this.options = options;
    }

    internal ChannelBackpressureStrategy<T> ToChannelStrategy<T>() => ToChannelStrategy<T>(options, null);

    internal static ChannelBackpressureStrategy<T> ToChannelStrategy<T>(BoundedChannelOptions options,
                                                                        Action<Exception, ChannelWriter<T>>? onErrorResume)
    {
        return BackpressureStrategy.FromChannel(() => Channel.CreateBounded<T>(options),
                                                onErrorResume: onErrorResume);
    }
}

public sealed class ChannelBackpressureStrategy<T>
{
    internal ChannelBackpressureStrategy(Func<Channel<T>> channelFactory,
                                             Action<T, ChannelWriter<T>> onNext,
                                             Action<Exception, ChannelWriter<T>>? onErrorResume)
    {
        ChannelFactory = channelFactory;
        OnNext = onNext;
        OnErrorResume = onErrorResume;
    }

    internal Func<Channel<T>> ChannelFactory { get; }
    internal Action<T, ChannelWriter<T>> OnNext { get; }
    internal Action<Exception, ChannelWriter<T>>? OnErrorResume { get; }
}

public static class R3ToAsyncObservableExtensions
{
    /// <summary>
    /// Converts an R3 <see cref="Observable{T}"/> into an <see cref="AsyncObservable{T}"/> delivering each notification
    /// synchronously on the emitting thread, which blocks until the async observer has finished processing it.
    /// A slow consumer therefore slows down the source itself.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this Observable<T> @this, BlockingBackpressureStrategy backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateBlocking(@this);
    }

    /// <summary>
    /// Converts an R3 <see cref="Observable{T}"/> into an <see cref="AsyncObservable{T}"/> buffering notifications through
    /// an unbounded channel: the source is never blocked, and a background loop drains the buffer into the async observer.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this Observable<T> @this, UnboundedChannelBackpressureStrategy backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateNonBlocking(@this, backpressureStrategy.ToChannelStrategy<T>());
    }

    /// <summary>
    /// Converts an R3 <see cref="Observable{T}"/> into an <see cref="AsyncObservable{T}"/> buffering notifications through
    /// a bounded channel drained by a background loop. Values are written with <see cref="ChannelWriter{T}.TryWrite"/>,
    /// so what happens when the buffer is full is governed by <see cref="BoundedChannelOptions.FullMode"/>: with drop modes
    /// values are discarded accordingly, while with <see cref="BoundedChannelFullMode.Wait"/> (the default) the write simply
    /// fails and the value is lost. Use <see cref="BackpressureStrategy.FromChannel{T}"/> with a custom onNext for waiting semantics.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this Observable<T> @this, BoundedChannelBackpressureStrategy backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateNonBlocking(@this, backpressureStrategy.ToChannelStrategy<T>());
    }

    /// <summary>
    /// Converts an R3 <see cref="Observable{T}"/> into an <see cref="AsyncObservable{T}"/> buffering notifications through
    /// a user-provided channel: the strategy supplies the channel, how values are written to it, and optionally how
    /// OnErrorResume notifications are handled. A background loop drains the channel into the async observer.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this Observable<T> @this, ChannelBackpressureStrategy<T> backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateNonBlocking(@this, backpressureStrategy);
    }

    static AsyncObservable<T> CreateBlocking<T>(Observable<T> source)
    {
        return AsyncObservable.Create<T>((observer, cancellationToken) =>
        {
            var subscription = source.Subscribe(new BlockingObserver<T>(observer));
            return new ValueTask<IAsyncDisposable>(new SubscriptionAsyncDisposable(subscription));
        });
    }

    static AsyncObservable<T> CreateNonBlocking<T>(Observable<T> source, ChannelBackpressureStrategy<T> backpressureStrategy)
    {
        return AsyncObservable.CreateAsBackgroundJob<T>(async (observer, cancellationToken) =>
        {
            var channel = backpressureStrategy.ChannelFactory();

            using var subscription = source.Subscribe(new ChannelObserver<T>(channel.Writer,
                                                                             backpressureStrategy.OnNext,
                                                                             backpressureStrategy.OnErrorResume));

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
