using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeContainsAsyncTest
{
    [Fact]
    public async Task SubscribeContainsAsync_ContainsValue_ReturnsTrue()
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

        await using var subscription = await source.SubscribeContainsAsync(2);
        var result = await subscription.GetValueAsync();
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeContainsAsync_DoesNotContainValue_ReturnsFalse()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeContainsAsync(2);
        var result = await subscription.GetValueAsync();
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task SubscribeContainsAsync_SubscribesEagerly_BeforeValueIsAwaited()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeContainsAsync(42);

        await subject.OnNextAsync(42, CancellationToken.None);

        var result = await subscription.GetValueAsync();
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeContainsAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeContainsAsync(42);

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        await subject.OnNextAsync(42, CancellationToken.None);
    }
}
