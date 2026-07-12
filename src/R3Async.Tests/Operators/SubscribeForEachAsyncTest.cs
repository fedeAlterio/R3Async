using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeForEachAsyncTest
{
    [Fact]
    public async Task SubscribeForEachAsync_AsyncCallback_InvokedForEachValue()
    {
        var results = new List<int>();
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

        await using var subscription = await source.SubscribeForEachAsync(async (x, token) => results.Add(x));
        var completed = await subscription.GetValueAsync();

        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task SubscribeForEachAsync_SyncCallback_InvokedForEachValue()
    {
        var results = new List<int>();
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

        await using var subscription = await source.SubscribeForEachAsync(results.Add);
        var completed = await subscription.GetValueAsync();

        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task SubscribeForEachAsync_SubscribesEagerly_BeforeCompletionIsAwaited()
    {
        var subject = Subject.Create<int>();
        var results = new List<int>();

        var subscription = await subject.Values.SubscribeForEachAsync(results.Add);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);

        // Values pushed before completion must already have been observed.
        results.ShouldBe(new[] { 1, 2 });

        await subject.OnCompletedAsync(Result.Success);
        await subscription.GetValueAsync();
    }

    [Fact]
    public async Task SubscribeForEachAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeForEachAsync(_ => { });

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnCompletedAsync(Result.Success);
    }
}
