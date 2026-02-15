using Playground.Common;
using R3Async;
using R3Async.Subjects;

namespace Playground.ServiceA.Services;

public interface IChatService
{
    ValueTask<IAsyncDisposableReference<ISubject<ChatMessage>>> GetOrCreateChatRoom(ChatRoomId id, CancellationToken cancellationToken);
}