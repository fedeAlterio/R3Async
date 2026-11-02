using R3Async.Internals;
using Shouldly;

namespace R3Async.Tests.Internals;

public class AsyncGateTests
{
    [Fact]
    public async Task LockAsync_IsReentrantWithinTheSameFlow()
    {
        var gate = new AsyncGate();

        using (await gate.LockAsync())
        using (await gate.LockAsync())
        {
        }

        // Fully released: another flow can acquire.
        using (await gate.LockAsync())
        {
        }
    }

    [Fact]
    public void LockAsync_CompletesSynchronouslyWhenFree()
    {
        var gate = new AsyncGate();

        var pending = gate.LockAsync();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        pending.Result.Dispose();
        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_WithCanceledTokenFailsEvenWhenFree()
    {
        var gate = new AsyncGate();

        await Should.ThrowAsync<OperationCanceledException>(async () => await gate.LockAsync(new CancellationToken(true)));

        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_ReentrantSucceedsEvenWithCanceledToken()
    {
        var gate = new AsyncGate();

        using (await gate.LockAsync())
        using (await gate.LockAsync(new CancellationToken(true)))
        {
        }

        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_FlowForkedWhileHeldIsNotReentrantOnceTheHolderReleases()
    {
        var gate = new AsyncGate();
        var otherFlowHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> forkedTryLock;

        using (await gate.LockAsync())
        {
            forkedTryLock = Task.Run(async () =>
            {
                await otherFlowHolds.Task;
                var acquired = gate.TryLock(out var forkedReleaser);
                if (acquired)
                {
                    forkedReleaser.Dispose();
                }

                return acquired;
            });
        }

        var otherReleaser = await Task.Run(() => gate.LockAsync().AsTask());
        otherFlowHolds.SetResult();

        (await forkedTryLock).ShouldBeFalse();
        otherReleaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_WaitersAcquireInArrivalOrder()
    {
        var gate = new AsyncGate();
        var holder = await await LockFromAnotherFlow(gate);
        var first = await LockFromAnotherFlow(gate);
        var second = await LockFromAnotherFlow(gate);

        first.IsCompleted.ShouldBeFalse();
        holder.Dispose();
        var firstReleaser = await first;
        second.IsCompleted.ShouldBeFalse();
        firstReleaser.Dispose();
        (await second).Dispose();

        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_CanceledWaiterLeavesTheQueueAndTheNextOneAcquires()
    {
        var gate = new AsyncGate();
        using var cts = new CancellationTokenSource();
        var holder = await await LockFromAnotherFlow(gate);
        var canceled = await LockFromAnotherFlow(gate, cts.Token);
        var next = await LockFromAnotherFlow(gate);

        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(canceled);
        next.IsCompleted.ShouldBeFalse();
        holder.Dispose();
        (await next).Dispose();

        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_ReusedWaitersKeepHandingTheGateOver()
    {
        var gate = new AsyncGate();

        for (var i = 0; i < 5; i++)
        {
            var holder = await await LockFromAnotherFlow(gate);
            var waiting = await LockFromAnotherFlow(gate);
            waiting.IsCompleted.ShouldBeFalse();
            holder.Dispose();
            (await waiting).Dispose();
        }

        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_LongQueueOfImmediateReleasesDoesNotNestWithoutBound()
    {
        var gate = new AsyncGate();
        var holder = await await LockFromAnotherFlow(gate);
        var released = new List<Task>();
        for (var i = 0; i < 5000; i++)
        {
            Task<Task> started;
            using (ExecutionContext.SuppressFlow())
            {
                started = Task.Factory.StartNew(() => AcquireAndRelease(gate), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            }

            released.Add(await started);
        }

        holder.Dispose();
        await Task.WhenAll(released);

        gate.TryLock(out var releaser).ShouldBeTrue();
        releaser.Dispose();

        static async Task AcquireAndRelease(AsyncGate gate) => (await gate.LockAsync()).Dispose();
    }

    static async Task<Task<AsyncGate.Releaser>> LockFromAnotherFlow(AsyncGate gate, CancellationToken cancellationToken = default)
    {
        Task<Task<AsyncGate.Releaser>> started;
        using (ExecutionContext.SuppressFlow())
        {
            started = Task.Factory.StartNew(() => gate.LockAsync(cancellationToken).AsTask(), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }

        return await started;
    }

    [Fact]
    public async Task LockAsync_MutualExclusionAcrossFlows()
    {
        var gate = new AsyncGate();
        var counter = 0;
        var maxConcurrent = 0;

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                using (await gate.LockAsync())
                {
                    var current = Interlocked.Increment(ref counter);
                    InterlockedMax(ref maxConcurrent, current);
                    Interlocked.Decrement(ref counter);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        maxConcurrent.ShouldBe(1);

        static void InterlockedMax(ref int location, int value)
        {
            int current;
            while ((current = Volatile.Read(ref location)) < value &&
                   Interlocked.CompareExchange(ref location, value, current) != current)
            {
            }
        }
    }
}
