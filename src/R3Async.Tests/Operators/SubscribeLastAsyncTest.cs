using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeLastAsyncTest
{
    [Fact]
    public async Task SubscribeLastAsync_ReturnsLastMatching()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(4, token);
                await observer.OnNextAsync(5, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeLastAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(4);
    }

    [Fact]
    public async Task SubscribeLastAsync_NoMatch_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeLastAsync(x => x % 2 == 0);
        await Should.ThrowAsync<InvalidOperationException>(async () => await subscription.GetValueAsync());
    }

    [Fact]
    public async Task SubscribeLastAsync_SubscribesEagerly_BeforeValueIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeLastAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(42, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(42);
    }

    [Fact]
    public async Task SubscribeLastAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeLastAsync();

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnNextAsync(1, CancellationToken.None);
    }
}
