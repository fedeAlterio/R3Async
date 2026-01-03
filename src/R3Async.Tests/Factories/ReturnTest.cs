using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class ReturnTest
{
    [Fact]
    public async Task Return_EmitsValueAndCompletion()
    {
        var obs = AsyncObservable.Return(42);

        var results = new List<int>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task Return_DisposeCancelsOnNext()
    {
        var obs = AsyncObservable.Return(1);

        var canceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = await obs.SubscribeAsync(async (x, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                canceledTcs.SetResult(true);
            }
        }, CancellationToken.None);

        await subscription.DisposeAsync();

        (await canceledTcs.Task).ShouldBeTrue();
    }

    [Fact]
    public async Task Return_ReentrantDispose_DoesNotDeadlock()
    {
        var obs = AsyncObservable.Return(7);

        var onNextCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncDisposable? subscription = null;
        subscription = await obs.SubscribeAsync(async (x, token) =>
        {
            while (subscription is null) await Task.Yield();
            await subscription.DisposeAsync();
            onNextCalled.SetResult(true);
        }, CancellationToken.None);

        await onNextCalled.Task;
        (true).ShouldBeTrue();
    }
}
