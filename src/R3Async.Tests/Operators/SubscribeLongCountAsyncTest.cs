using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeLongCountAsyncTest
{
    [Fact]
    public async Task SubscribeLongCountAsync_ReturnsMatchingCount()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeLongCountAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(2L);
    }

    [Fact]
    public async Task SubscribeLongCountAsync_SubscribesEagerly_BeforeCompletionIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeLongCountAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(2L);
    }

    [Fact]
    public async Task SubscribeLongCountAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeLongCountAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnCompletedAsync(Result.Success);
    }
}
