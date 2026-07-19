using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace R3Async;

public static class SystemToAsyncObservableExtensions
{
    /// <summary>
    /// Converts a <see cref="IObservable{T}"/> into an <see cref="AsyncObservable{T}"/> delivering each notification
    /// synchronously on the emitting thread, which blocks until the async observer has finished processing it.
    /// A slow consumer therefore slows down the source itself. OnError is mapped to a failure completion.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this IObservable<T> @this, BlockingBackpressureStrategy backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateBlocking(@this);
    }

    /// <summary>
    /// Converts a <see cref="IObservable{T}"/> into an <see cref="AsyncObservable{T}"/> buffering notifications through
    /// an unbounded channel: the source is never blocked, and a background loop drains the buffer into the async observer.
    /// OnError is mapped to a failure completion.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this IObservable<T> @this, UnboundedChannelBackpressureStrategy backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateNonBlocking(@this, backpressureStrategy.ToChannelStrategy<T>());
    }

    /// <summary>
    /// Converts a <see cref="IObservable{T}"/> into an <see cref="AsyncObservable{T}"/> buffering notifications through
    /// a bounded channel drained by a background loop. Values are written with <see cref="ChannelWriter{T}.TryWrite"/>,
    /// so what happens when the buffer is full is governed by <see cref="BoundedChannelOptions.FullMode"/>: with drop modes
    /// values are discarded accordingly, while with <see cref="BoundedChannelFullMode.Wait"/> (the default) the write simply
    /// fails and the value is lost. Use <see cref="BackpressureStrategy.FromChannel{T}"/> with a custom onNext for waiting
    /// semantics. OnError is mapped to a failure completion.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this IObservable<T> @this, BoundedChannelBackpressureStrategy backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateNonBlocking(@this, backpressureStrategy.ToChannelStrategy<T>());
    }

    /// <summary>
    /// Converts a <see cref="IObservable{T}"/> into an <see cref="AsyncObservable{T}"/> buffering notifications through
    /// a user-provided channel: the strategy supplies the channel and how values are written to it. A background loop
    /// drains the channel into the async observer. The IObservable grammar has no resumable-error channel, so the
    /// strategy's onErrorResume hook is never invoked; OnError is mapped to a failure completion.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this IObservable<T> @this, ChannelBackpressureStrategy<T> backpressureStrategy)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));
        if (backpressureStrategy is null)
            throw new ArgumentNullException(nameof(backpressureStrategy));

        return CreateNonBlocking(@this, backpressureStrategy);
    }

    static AsyncObservable<T> CreateBlocking<T>(IObservable<T> source)
    {
        return AsyncObservable.Create<T>((observer, cancellationToken) =>
        {
            var subscription = source.Subscribe(new BlockingObserver<T>(observer));
            return new ValueTask<IAsyncDisposable>(new SubscriptionAsyncDisposable(subscription));
        });
    }

    static AsyncObservable<T> CreateNonBlocking<T>(IObservable<T> source, ChannelBackpressureStrategy<T> backpressureStrategy)
    {
        return AsyncObservable.CreateAsBackgroundJob<T>(async (observer, cancellationToken) =>
        {
            var channel = backpressureStrategy.ChannelFactory();

            using var subscription = source.Subscribe(new ChannelObserver<T>(channel.Writer, backpressureStrategy.OnNext));

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

    sealed class BlockingObserver<T>(AsyncObserver<T> observer) : IObserver<T>
    {
        public void OnNext(T value) => WaitSynchronously(observer.OnNextAsync(value, CancellationToken.None));

        public void OnError(Exception error) => WaitSynchronously(observer.OnCompletedAsync(Result.Failure(error)));

        public void OnCompleted() => WaitSynchronously(observer.OnCompletedAsync(Result.Success));

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

    sealed class ChannelObserver<T>(ChannelWriter<T> writer, Action<T, ChannelWriter<T>> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value, writer);

        public void OnError(Exception error) => writer.TryComplete(error);

        public void OnCompleted() => writer.TryComplete();
    }
}
