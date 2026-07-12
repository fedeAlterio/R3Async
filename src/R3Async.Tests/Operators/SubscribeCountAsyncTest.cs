using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeCountAsyncTest
{
    [Fact]
    public async Task SubscribeCountAsync_ReturnsMatchingCount()
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

        await using var subscription = await source.SubscribeCountAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(2);
    }

    [Fact]
    public async Task SubscribeCountAsync_SubscribesEagerly_BeforeCompletionIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeCountAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(2);
    }

    [Fact]
    public async Task SubscribeCountAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeCountAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        // The underlying subscription is gone, so completion after dispose must be silently ignored.
        await subject.OnCompletedAsync(Result.Success);
    }
}
