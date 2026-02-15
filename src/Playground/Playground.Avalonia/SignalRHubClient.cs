using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Playground.Common;
using R3Async;

namespace Playground.Avalonia;

public sealed class SignalRHubClient
{
    readonly RefCountValue<HubConnection> _sharedConnection = new(async token =>
    {
        var connection = new HubConnectionBuilder()
                         .WithUrl("http://localhost:5062/hub")
                         .Build();

        await connection.StartAsync(token);
        return AsyncDisposableValue.From(connection);
    });

    public async IAsyncEnumerable<ChatMessage> JoinRoom(ChatRoomId roomId, string user, IAsyncEnumerable<ChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var hubRef = await _sharedConnection.GetAsync(cancellationToken);
        await foreach (var message in hubRef.Value.StreamAsync<ChatMessage>("JoinRoom", roomId, user, messages, cancellationToken: cancellationToken))
        {
            yield return message;
        }
    }
}