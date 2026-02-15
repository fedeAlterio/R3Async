using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Playground.Common;
using R3Async;
using SignalsDotnet;

namespace Playground.Avalonia;

public sealed class SignalRHubClient
{
    readonly RefCountedLazy<HubConnection> _sharedConnection;

    public SignalRHubClient()
    {
       _sharedConnection = new(async token =>
        {
            var connection = new HubConnectionBuilder()
                             .WithUrl("http://localhost:5062/hub")
                             .Build();

            await connection.StartAsync(token);
            _isConnected.Value = true;
            return new()
            {
                Value = connection,
                Disposable = AsyncDisposable.Create(async () =>
                {
                    await connection.DisposeAsync();
                    _isConnected.Value = false; 
                })
            };
        });
    }

    readonly Signal<bool> _isConnected = new(false);
    public ISignal<bool> IsConnected => _isConnected;

    public async IAsyncEnumerable<ChatMessage> GetChatMessages(ChatRoomId roomId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var hubRef = await _sharedConnection.GetAsync(cancellationToken);
        var connection = hubRef.Value;
        var stream = connection.StreamAsync<ChatMessage>("GetChatMessages", roomId, cancellationToken);
        await foreach (var message in stream)
        {
            yield return message;
        }
    }

    public async ValueTask JoinRoom(ChatRoomId roomId, string user, IAsyncEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        await using var hubRef = await _sharedConnection.GetAsync(cancellationToken);
        await hubRef.Value.InvokeAsync("JoinRoom", roomId, user, messages, cancellationToken: cancellationToken);
    }
}