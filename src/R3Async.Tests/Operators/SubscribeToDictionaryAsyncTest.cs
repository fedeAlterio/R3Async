using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeToDictionaryAsyncTest
{
    [Fact]
    public async Task SubscribeToDictionaryAsync_KeySelectorOnly_ReturnsDictionary()
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

        await using var subscription = await source.SubscribeToDictionaryAsync(x => x.ToString());
        var result = await subscription.GetValueAsync();
        result.ShouldBe(new Dictionary<string, int> { ["1"] = 1, ["2"] = 2 });
    }

    [Fact]
    public async Task SubscribeToDictionaryAsync_KeyAndElementSelector_ReturnsDictionary()
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

        await using var subscription = await source.SubscribeToDictionaryAsync(x => x.ToString(), x => x * 10);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(new Dictionary<string, int> { ["1"] = 10, ["2"] = 20 });
    }

    [Fact]
    public async Task SubscribeToDictionaryAsync_SubscribesEagerly_BeforeCompletionIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeToDictionaryAsync(x => x.ToString());

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(new Dictionary<string, int> { ["1"] = 1 });
    }

    [Fact]
    public async Task SubscribeToDictionaryAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeToDictionaryAsync(x => x.ToString());

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnCompletedAsync(Result.Success);
    }
}
