using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeFirstAsyncTest
{
    [Fact]
    public async Task SubscribeFirstAsync_ReturnsFirstMatching()
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

        await using var subscription = await source.SubscribeFirstAsync(x => x % 2 == 0);
        var result = await subscription.GetValueAsync();
        result.ShouldBe(2);
    }

    [Fact]
    public async Task SubscribeFirstAsync_NoPredicate_ReturnsFirstElement()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(5, token);
                await observer.OnNextAsync(6, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeFirstAsync();
        var result = await subscription.GetValueAsync();
        result.ShouldBe(5);
    }

    [Fact]
    public async Task SubscribeFirstAsync_NoMatch_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await source.SubscribeFirstAsync(x => x % 2 == 0);
        await Should.ThrowAsync<InvalidOperationException>(async () => await subscription.GetValueAsync());
    }

    [Fact]
    public async Task SubscribeFirstAsync_SubscribesEagerly_BeforeValueIsAwaited()
    {
        var subject = Subject.Create<int>();

        // Subscribing must happen as part of this call, not deferred until the value is awaited,
        // otherwise a value published between subscribing and awaiting would be lost.
        var subscription = await subject.Values.SubscribeFirstAsync();

        await subject.OnNextAsync(42, CancellationToken.None);

        var result = await subscription.GetValueAsync();
        result.ShouldBe(42);
    }

    [Fact]
    public async Task SubscribeFirstAsync_Dispose_UnsubscribesAndFaultsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeFirstAsync();

        await subscription.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());

        // The underlying subscription is gone, so further pushes must be silently ignored.
        await subject.OnNextAsync(1, CancellationToken.None);
    }

    [Fact]
    public async Task SubscribeFirstAsync_Cancellation_ThrowsOperationCanceledException()
    {
        bool disposed = false;
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            return AsyncDisposable.Create(async () =>
            {
                disposed = true;
            });
        });

        using var cts = new CancellationTokenSource();
        var subscription = await source.SubscribeFirstAsync(cts.Token);
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await subscription.GetValueAsync());
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeFirstAsync_GetResultAsync_Timeout_ThrowsTimeoutException()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeFirstAsync();

        // No value is ever pushed, so GetResultAsync must be bounded by the timeout rather than hang forever.
        await Should.ThrowAsync<TimeoutException>(async () =>
            await subscription.GetValueAsync(timeout: TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task SubscribeFirstAsync_GetResultAsync_CancellationToken_ThrowsOperationCanceledException()
    {
        var subject = Subject.Create<int>();    
        var subscription = await subject.Values.SubscribeFirstAsync();

        using var cts = new CancellationTokenSource();
        var wait = subscription.GetValueAsync(cts.Token).AsTask();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await wait);
    }

    [Fact]
    public async Task SubscribeFirstAsync_GetResultAsync_TimeoutDoesNotFire_ReturnsValue()
    {
        var subject = Subject.Create<int>();
        var subscription = await subject.Values.SubscribeFirstAsync();

        await subject.OnNextAsync(7, CancellationToken.None);

        var result = await subscription.GetValueAsync(timeout: TimeSpan.FromSeconds(30));
        result.ShouldBe(7);
    }
}
