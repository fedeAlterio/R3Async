using Playground.Common;
using R3Async;
using R3Async.Subjects;
using StackExchange.Redis;

namespace Playground.ServiceA;

public class RedisChatService : IChatService
{
    readonly RefCountTable<ChatRoomId, ISubject<ChatMessage>> _table;

    public RedisChatService(IConnectionMultiplexer connectionMultiplexer)
    {
        _table = new(async (roomId, _) =>
        {
            var subscriber = connectionMultiplexer.GetSubscriber();
            var disposable = AsyncDisposable.Create(async () => await subscriber.UnsubscribeAllAsync());
            try
            {
                var subject = subscriber.ToSubject<ChatMessage>(new(roomId.Name, RedisChannel.PatternMode.Auto));
                return new AsyncDisposableValue<ISubject<ChatMessage>>
                {
                    Value = subject,
                    Disposable = disposable
                };
            }
            catch
            {
                await disposable.DisposeAsync();
                throw;
            }
        });
    }

    public ValueTask<IAsyncDisposableReference<ISubject<ChatMessage>>> GetOrCreateChatRoom(ChatRoomId id, CancellationToken cancellationToken)
    {
        return _table.GetOrCreateAsync(id, cancellationToken);
    }
}