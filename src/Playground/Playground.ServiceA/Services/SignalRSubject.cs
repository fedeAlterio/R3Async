using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using R3Async;
using R3Async.Subjects;

namespace Playground.ServiceA.Services;

public sealed class SignalRSubject<T> : ISubject<T> where T : class
{
    readonly HubConnection _connection;
    readonly string _channel;

    public SignalRSubject(HubConnection connection, string channel)
    {
        _connection = connection;
        _channel = channel;

        Values = AsyncObservable.Create<T>(async (observer, token) =>
        {
            return await connection.StreamAsync<string>("Subscribe", _channel)
                                   .ToAsyncObservable()
                                   .Select(x => JsonSerializer.Deserialize<Notification>(x)!.ForwardTo(observer, token))
                                   .SubscribeAsync(static delegate {}, cancellationToken: token);
        });
    }

    public AsyncObservable<T> Values { get; }

    public ValueTask OnNextAsync(T value, CancellationToken cancellationToken) => ForwardNotification(Notification.FromOnNext(value),                       cancellationToken);
    public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken) => ForwardNotification(Notification.FromOnErrorResume(error), cancellationToken);
    public ValueTask OnCompletedAsync(Result result) => ForwardNotification(Notification.FromOnCompleted(result),                                           CancellationToken.None);
    async ValueTask ForwardNotification(Notification notification, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(notification);
        await _connection.InvokeAsync("Publish", _channel, json, cancellationToken: cancellationToken);
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