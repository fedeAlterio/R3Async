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
