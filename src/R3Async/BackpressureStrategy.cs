using System;
using System.Threading.Channels;

namespace R3Async;

/// <summary>
/// Factory methods for building backpressure strategies that govern how a synchronous source (a <see cref="IObservable{T}"/>
/// or an R3 <c>Observable&lt;T&gt;</c>) is converted into an <see cref="AsyncObservable{T}"/>, i.e. how synchronously-pushed
/// values are handed off to a potentially slower async observer.
/// </summary>
public static class BackpressureStrategy
{
    /// <summary>
    /// A strategy that delivers each notification synchronously on the emitting thread, blocking it until the async
    /// observer has finished processing the value. The source itself is slowed down by a slow consumer.
    /// </summary>
    public static BlockingBackpressureStrategy Blocking { get; } = new();

    /// <summary>
    /// Creates a strategy that buffers notifications in an unbounded <see cref="Channel{T}"/>: the source is never
    /// blocked, and a background loop drains the channel into the async observer.
    /// </summary>
    /// <param name="options">Optional channel configuration; when <c>null</c>, default unbounded channel options are used.</param>
    public static UnboundedChannelBackpressureStrategy FromUnboundedChannel(UnboundedChannelOptions? options = null)
    {
        return new(options);
    }

    /// <summary>
    /// Creates a strategy that buffers notifications in an unbounded <see cref="Channel{T}"/>, routing resumable errors
    /// from the source to <paramref name="onErrorResume"/> instead of the <see cref="UnhandledExceptionHandler"/>.
    /// </summary>
    /// <param name="onErrorResume">Callback invoked with a resumable error from the source and the channel writer.</param>
    /// <param name="options">Optional channel configuration; when <c>null</c>, default unbounded channel options are used.</param>
    public static ChannelBackpressureStrategy<T> FromUnboundedChannel<T>(Action<Exception, ChannelWriter<T>> onErrorResume,
                                                                         UnboundedChannelOptions? options = null)
    {
        if (onErrorResume is null)
            throw new ArgumentNullException(nameof(onErrorResume));

        return UnboundedChannelBackpressureStrategy.ToChannelStrategy(options, onErrorResume);
    }

    /// <summary>
    /// Creates a strategy that buffers notifications in a bounded <see cref="Channel{T}"/> of the given capacity,
    /// drained by a background loop. Values are written with <see cref="ChannelWriter{T}.TryWrite"/>, so with the
    /// default <see cref="BoundedChannelFullMode.Wait"/> mode, values are silently dropped when the buffer is full.
    /// </summary>
    /// <param name="capacity">The channel's bounded capacity.</param>
    public static BoundedChannelBackpressureStrategy FromBoundedChannel(int capacity)
    {
        return new(new BoundedChannelOptions(capacity));
    }

    /// <summary>
    /// Creates a strategy that buffers notifications in a bounded <see cref="Channel{T}"/> configured via <paramref name="options"/>,
    /// drained by a background loop. Values are written with <see cref="ChannelWriter{T}.TryWrite"/>, so what happens
    /// when the buffer is full is governed by <see cref="BoundedChannelOptions.FullMode"/>.
    /// </summary>
    public static BoundedChannelBackpressureStrategy FromBoundedChannel(BoundedChannelOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        return new(options);
    }

    /// <summary>
    /// Creates a strategy that buffers notifications in a bounded <see cref="Channel{T}"/> of the given capacity, routing
    /// resumable errors from the source to <paramref name="onErrorResume"/> instead of the <see cref="UnhandledExceptionHandler"/>.
    /// </summary>
    public static ChannelBackpressureStrategy<T> FromBoundedChannel<T>(Action<Exception, ChannelWriter<T>> onErrorResume, int capacity)
    {
        return FromBoundedChannel(onErrorResume, new BoundedChannelOptions(capacity));
    }

    /// <summary>
    /// Creates a strategy that buffers notifications in a bounded <see cref="Channel{T}"/> configured via <paramref name="options"/>,
    /// routing resumable errors from the source to <paramref name="onErrorResume"/> instead of the <see cref="UnhandledExceptionHandler"/>.
    /// </summary>
    public static ChannelBackpressureStrategy<T> FromBoundedChannel<T>(Action<Exception, ChannelWriter<T>> onErrorResume, BoundedChannelOptions options)
    {
        if (onErrorResume is null)
            throw new ArgumentNullException(nameof(onErrorResume));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        return BoundedChannelBackpressureStrategy.ToChannelStrategy(options, onErrorResume);
    }

    /// <summary>
    /// Creates a fully customizable channel-based strategy: <paramref name="channelFactory"/> supplies the channel, and
    /// <paramref name="onNext"/> controls how each value is written to it (by default, <see cref="ChannelWriter{T}.TryWrite"/>,
    /// which silently drops the value if the write fails). <paramref name="onErrorResume"/>, when provided, receives
    /// resumable errors from the source instead of routing them to the <see cref="UnhandledExceptionHandler"/>.
    /// </summary>
    public static ChannelBackpressureStrategy<T> FromChannel<T>(Func<Channel<T>> channelFactory,
                                                                Action<T, ChannelWriter<T>>? onNext = null,
                                                                Action<Exception, ChannelWriter<T>>? onErrorResume = null)
    {
        if (channelFactory is null)
            throw new ArgumentNullException(nameof(channelFactory));

        return new(channelFactory, onNext ?? (static (x, c) => c.TryWrite(x)), onErrorResume);
    }
}

/// <summary>
/// A backpressure strategy that delivers notifications synchronously, blocking the emitting thread until the async
/// observer finishes processing each value. Obtained via <see cref="BackpressureStrategy.Blocking"/>.
/// </summary>
public sealed class BlockingBackpressureStrategy
{
    internal BlockingBackpressureStrategy()
    {
    }
}

/// <summary>
/// A backpressure strategy that buffers notifications in an unbounded <see cref="Channel{T}"/>. Obtained via
/// <see cref="BackpressureStrategy.FromUnboundedChannel(UnboundedChannelOptions?)"/>.
/// </summary>
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

/// <summary>
/// A backpressure strategy that buffers notifications in a bounded <see cref="Channel{T}"/>. Obtained via
/// <see cref="BackpressureStrategy.FromBoundedChannel(int)"/> or <see cref="BackpressureStrategy.FromBoundedChannel(BoundedChannelOptions)"/>.
/// </summary>
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

/// <summary>
/// A fully customizable channel-based backpressure strategy, specifying the channel to use, how values are written to
/// it, and optionally how resumable errors from the source are handled. Obtained via <see cref="BackpressureStrategy.FromChannel{T}"/>
/// or the generic overloads of <see cref="BackpressureStrategy.FromUnboundedChannel{T}"/> / <see cref="BackpressureStrategy.FromBoundedChannel{T}(Action{Exception, ChannelWriter{T}}, int)"/>.
/// </summary>
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
