using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Playground.Common;
using R3Async;

namespace Playground.ServiceA;

public class AppHub(IChatService chatService) : Hub
{
    public async IAsyncEnumerable<ChatMessage> JoinRoom(ChatRoomId roomId, string user, IAsyncEnumerable<ChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var chatRef = await chatService.GetOrCreateChatRoom(roomId, cancellationToken);
        var chat = chatRef.Value;
        var channel = Channel.CreateUnbounded<ChatMessage>();
        
        await using var chatToChannelPipe = await chat.Values.PipeAsync(channel.Writer, cancellationToken: cancellationToken);
        await chat.OnNextAsync(new ChatMessage(user, $"{user} joined the room"), cancellationToken);
        try
        {
            await using var messagesToChatPipe = await messages.ToAsyncObservable().PipeAsync(chat);
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }
        }
        finally
        {
            await chat.OnNextAsync(new ChatMessage(user, $"{user} left the room"), cancellationToken);
        }
    }
}
