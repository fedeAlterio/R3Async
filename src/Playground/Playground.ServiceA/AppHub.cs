using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Playground.Common;
using Playground.ServiceA.Services;
using R3Async;

namespace Playground.ServiceA;

public class AppHub(IChatService chatService) : Hub
{
    public async IAsyncEnumerable<ChatMessage> GetChatMessages(ChatRoomId roomId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var chatRef = await chatService.GetOrCreateChatRoom(roomId, cancellationToken);
        var chatMessages = chatRef.Value;
        await foreach (var message in chatMessages.Values
                                          .ToAsyncEnumerable(static () => Channel.CreateUnbounded<ChatMessage>())
                                          .WithCancellation(cancellationToken))
        {
            yield return message;
        }
    }

    public async Task JoinRoom(ChatRoomId roomId, string user, IAsyncEnumerable<ChatMessage> messages)
    {
        var cancellationToken = Context.ConnectionAborted;
        await using var chatRef = await chatService.GetOrCreateChatRoom(roomId, cancellationToken);
        var chatMessages = chatRef.Value;
        await chatMessages.OnNextAsync(new ChatMessage(user, $"{user} joined the room"), cancellationToken);
        try
        {
            await foreach (var message in messages.WithCancellation(cancellationToken))
            {
                await chatMessages.OnNextAsync(message, cancellationToken);
            }
        }
        finally
        {
            await chatMessages.OnNextAsync(new ChatMessage(user, $"{user} left the room"), cancellationToken);
        }
    }
}
