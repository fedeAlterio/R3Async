using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeToListAsyncTest
{
    [Fact]
    public async Task SubscribeToListAsync_ReturnsAllValues()
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

        await using var subscription = await source.SubscribeToListAsync();
        var result = await subscription.GetValueAsync();
        result.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task SubscribeToListAsync_SubscribesEagerly_BeforeCompletionIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeToListAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task SubscribeToListAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeToListAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnCompletedAsync(Result.Success);
    }
}
