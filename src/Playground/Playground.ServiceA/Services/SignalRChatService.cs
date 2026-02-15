using Microsoft.AspNetCore.SignalR.Client;
using Playground.Common;
using R3Async;
using R3Async.Subjects;

namespace Playground.ServiceA.Services;

public sealed class SignalRChatService : IChatService
{
    readonly RefCountedLazy<HubConnection> _sharedConnection;

    public SignalRChatService(IConfiguration configuration)
    {
        _sharedConnection = new(async token =>
        {
            var url = configuration["services:pubSubSignalR:http:0"]!;
            var connectionBuilder = new HubConnectionBuilder()
                                    .WithUrl($"{url}/hub")
                                    .Build();   

            await connectionBuilder.StartAsync(token);
            return AsyncDisposableValue.From(connectionBuilder);
        });
    }

    public async ValueTask<IAsyncDisposableReference<ISubject<ChatMessage>>> GetOrCreateChatRoom(ChatRoomId id, CancellationToken cancellationToken)
    {
        var reference = await _sharedConnection.GetAsync(cancellationToken);
        try
        {
            var subject = new SignalRSubject<ChatMessage>(reference.Value, id.Name);
            return new AsyncDisposableValue<ISubject<ChatMessage>>
            {
                Value = subject,
                Disposable = reference
            };
        }
        catch
        {
            await reference.DisposeAsync();
            throw;
        }
    }
}