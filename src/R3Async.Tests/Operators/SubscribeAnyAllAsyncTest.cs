using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeAnyAllAsyncTest
{
    [Fact]
    public async Task SubscribeAnyAsync_MatchFound_ReturnsTrue()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeAnyAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeAnyAsync_NoMatch_ReturnsFalse()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeAnyAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task SubscribeAllAsync_AllMatch_ReturnsTrue()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(4, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeAllAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeAllAsync_OneDoesNotMatch_ReturnsFalse()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeAllAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task SubscribeAnyAsync_SubscribesEagerly_BeforeValueIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeAnyAsync();

        await subject.OnNextAsync(42, CancellationToken.None);

        var result = await subscription.GetValueAsync();
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeAnyAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeAnyAsync();

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnNextAsync(1, CancellationToken.None);
    }
}
