using Playground.Common;
using R3Async;
using R3Async.Subjects;

namespace Playground.ServiceA;

public interface IChatService
{
    ValueTask<IAsyncDisposableReference<ISubject<ChatMessage>>> GetOrCreateChatRoom(ChatRoomId id, CancellationToken cancellationToken);
}

public class InMemoryChatService : IChatService
{
    readonly RefCountTable<ChatRoomId, ISubject<ChatMessage>> _chatByRoom;

    public InMemoryChatService()
    {
        _chatByRoom = new(async (roomId, cancellationToken) =>
        {
            var chat = Subject.Create<ChatMessage>(new SubjectCreationOptions
            {
                IsStateless = false,
                PublishingOption = PublishingOption.Concurrent
            });

            return new()
            {
                Value = chat,
                Disposable = AsyncDisposable.Create(() => chat.OnCompletedAsync(Result.Success))
            };
        });
    }

    public ValueTask<IAsyncDisposableReference<ISubject<ChatMessage>>> GetOrCreateChatRoom(ChatRoomId id, CancellationToken cancellationToken)
    {
        return _chatByRoom.GetOrCreateAsync(id, cancellationToken);
    }
}