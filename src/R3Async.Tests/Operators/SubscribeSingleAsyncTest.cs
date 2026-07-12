using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeSingleAsyncTest
{
    [Fact]
    public async Task SubscribeSingleAsync_ReturnsSingleMatching()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeSingleAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(2);
    }

    [Fact]
    public async Task SubscribeSingleAsync_MoreThanOneMatch_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeSingleAsync(x => x % 2 == 0);
        await Should.ThrowAsync<InvalidOperationException>(async () => await subscription.GetValueAsync());
    }

    [Fact]
    public async Task SubscribeSingleAsync_NoMatch_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeSingleAsync(x => x % 2 == 0);
        await Should.ThrowAsync<InvalidOperationException>(async () => await subscription.GetValueAsync());
    }

    [Fact]
    public async Task SubscribeSingleAsync_SubscribesEagerly_BeforeValueIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeSingleAsync();

        await subject.OnNextAsync(42, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(42);
    }

    [Fact]
    public async Task SubscribeSingleAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeSingleAsync();

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnNextAsync(1, CancellationToken.None);
    }
}
