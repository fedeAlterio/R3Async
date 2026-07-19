using System;
using System.Threading.Channels;

namespace R3Async;

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
