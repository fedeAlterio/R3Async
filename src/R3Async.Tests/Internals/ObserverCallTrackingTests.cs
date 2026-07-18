using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Internals;

public class ObserverCallTrackingTests
{
    [Fact]
    public async Task NotificationFromFlowForkedInsideHandler_AfterChainUnwound_IsDelivered()
    {
        var source = new ManualSource<int>();
        var results = new List<int>();
        var firstHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? forked = null;

        await using var subscription = await source.SubscribeAsync(
            async (x, ct) =>
            {
                results.Add(x);
                if (x == 1)
                {
                    // The forked flow inherits this observer's call token via ExecutionContext.
                    forked = Task.Run(async () =>
                    {
                        await firstHandled.Task;
                        await source.Observer!.OnNextAsync(2, CancellationToken.None);
                    });
                }
            },
            CancellationToken.None);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        firstHandled.SetResult();
        await forked!;

        // With count-based tracking the forked flow's stale counter made every later call from it
        // look concurrent, so the value was silently dropped.
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DisposeWaitsForInFlightCallFromAnotherFlow()
    {
        var source = new ManualSource<int>();
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCompleted = false;

        var subscription = await source.SubscribeAsync(
            async (x, ct) =>
            {
                handlerEntered.SetResult();
                await releaseHandler.Task;
                handlerCompleted = true;
            },
            CancellationToken.None);

        var push = Task.Run(() => source.Observer!.OnNextAsync(1, CancellationToken.None).AsTask());
        await handlerEntered.Task;

        var dispose = subscription.DisposeAsync().AsTask();
        await Task.Delay(100);
        dispose.IsCompleted.ShouldBeFalse();

        releaseHandler.SetResult();
        await dispose;
        await push;
        handlerCompleted.ShouldBeTrue();
    }
}
