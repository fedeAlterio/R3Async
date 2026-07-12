using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeLastOrDefaultAsyncTest
{
    [Fact]
    public async Task SubscribeLastOrDefaultAsync_ReturnsLastMatching()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeLastOrDefaultAsync(x => x % 2 == 0, -1);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(4);
    }

    [Fact]
    public async Task SubscribeLastOrDefaultAsync_NoMatch_ReturnsDefaultValue()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeLastOrDefaultAsync(x => x % 2 == 0, -1);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(-1);
    }

    [Fact]
    public async Task SubscribeLastOrDefaultAsync_SubscribesEagerly_BeforeValueIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeLastOrDefaultAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(42, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(42);
    }

    [Fact]
    public async Task SubscribeLastOrDefaultAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeLastOrDefaultAsync();

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnNextAsync(1, CancellationToken.None);
    }
}
