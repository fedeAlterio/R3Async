using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeWaitCompletionAsyncTest
{
    [Fact]
    public async Task SubscribeWaitCompletionAsync_CompletesSuccessfully()
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

        await using var subscription = await source.SubscribeWaitCompletionAsync();
        await subscription.GetValueAsync();
    }

    [Fact]
    public async Task SubscribeWaitCompletionAsync_SourceFails_PropagatesException()
    {
        var expected = new InvalidOperationException("boom");
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeWaitCompletionAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await subscription.GetValueAsync());
        ex.ShouldBe(expected);
    }

    [Fact]
    public async Task SubscribeWaitCompletionAsync_SubscribesEagerly_BeforeCompletionIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeWaitCompletionAsync();

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        await subscription.GetValueAsync();
    }

    [Fact]
    public async Task SubscribeWaitCompletionAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeWaitCompletionAsync();

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnCompletedAsync(Result.Success);
    }
}
