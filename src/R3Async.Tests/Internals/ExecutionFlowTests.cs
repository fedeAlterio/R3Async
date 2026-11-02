using R3Async.Internals;
using Shouldly;

namespace R3Async.Tests.Internals;

public class ExecutionFlowTests
{
    [Fact(Timeout = 10000)]
    public async Task Contains_IsVisibleToNestedAsyncCallsAndRevertedOnReturn()
    {
        var owner = new object();

        async Task<bool> Nested()
        {
            await Task.Yield();
            return ExecutionFlow.Contains(owner);
        }

        async Task<bool> Outer()
        {
            ExecutionFlow.Enter(owner);
            return await Nested();
        }

        (await Outer()).ShouldBeTrue();
        ExecutionFlow.Contains(owner).ShouldBeFalse();
    }

    [Fact(Timeout = 10000)]
    public async Task Enter_SkipsFinishedTokensSoRepeatedAcquisitionsDoNotGrowTheFlow()
    {
        var gate = new AsyncGate();
        var depthBefore = ExecutionFlow.Depth;

        for (var i = 0; i < 1000; i++)
        {
            using (await gate.LockAsync())
            {
            }
        }

        (ExecutionFlow.Depth - depthBefore).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Contains_IsFalseForNull()
    {
        ExecutionFlow.Contains(null).ShouldBeFalse();
    }
}
