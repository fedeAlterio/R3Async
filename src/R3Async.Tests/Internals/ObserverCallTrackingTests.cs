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
}
