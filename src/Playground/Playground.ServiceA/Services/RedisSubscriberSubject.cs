using System.Text.Json;
using R3Async;
using R3Async.Subjects;
using StackExchange.Redis;

namespace Playground.ServiceA.Services;

sealed class RedisSubscriberSubject<T> : ISubject<T> where T : class
{
    readonly ISubscriber _subscriber;
    readonly RedisChannel _channel;

    public RedisSubscriberSubject(ISubscriber subscriber, 
                                  RedisChannel channel)
    {
        _subscriber = subscriber;
        _channel = channel;

        Values = AsyncObservable.Create<T>((observer, _) =>
        {
            var messageQueue = subscriber.Subscribe(_channel);
            messageQueue.OnMessage(async message =>
            {
                var notification = JsonSerializer.Deserialize<Notification>((string)message.Message!)!;
                await notification.ForwardTo(observer, CancellationToken.None);
            });

            var disposable = AsyncDisposable.Create(async () => await messageQueue.UnsubscribeAsync());
            return new(disposable);
        });
    }

    public AsyncObservable<T> Values { get; }

    public ValueTask OnNextAsync(T value, CancellationToken cancellationToken) => ForwardNotification(Notification.FromOnNext(value));
    public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken) => ForwardNotification(Notification.FromOnErrorResume(error));
    public ValueTask OnCompletedAsync(Result result) => ForwardNotification(Notification.FromOnCompleted(result));
    async ValueTask ForwardNotification(Notification notification)
    {
        var json = JsonSerializer.Serialize(notification);
        await _subscriber.PublishAsync(_channel, json);
    }

    record Notification
    {
        public static Notification FromOnNext(T value) => new()
        {
            Value = value,
            ErrorMessage = null,
            IsCompleted = false
        };

        public static Notification FromOnErrorResume(Exception exception) => new()
        {
            Value = null,
            ErrorMessage = exception.Message,
            IsCompleted = false
        };

        public static Notification FromOnCompleted(Result result) => new()
        {
            Value = null,
            ErrorMessage = result.Exception?.Message,
            IsCompleted = true
        };

        public string? ErrorMessage { get; init; }
        public T? Value { get; init; }
        public bool IsCompleted { get; init; }

        public ValueTask ForwardTo(AsyncObserver<T> observer, CancellationToken cancellationToken) => (IsCompleted, ErrorMessage) switch
        {
            (false, null) => observer.OnNextAsync(Value!, cancellationToken),
            (false, not null) => observer.OnErrorResumeAsync(new(ErrorMessage), cancellationToken),
            (true, null) => observer.OnCompletedAsync(Result.Success),
            (true, not null) => observer.OnCompletedAsync(Result.Failure(new(ErrorMessage)))
        };
    }
}

public static class RedisSubscriberEx
{
    public static ISubject<T> ToSubject<T>(this ISubscriber subscriber, RedisChannel channel) where T : class => new RedisSubscriberSubject<T>(subscriber, channel);
}