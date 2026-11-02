using Shouldly;

namespace R3Async.Tests.Operators;

public class MergeEnumerableSubscriptionTest
{
    [Fact(Timeout = 10000)]
    public async Task MergeEnumerable_SubscribeReturnsOnceEverySourceIsSubscribed()
    {
        var subscribed = 0;
        var source = AsyncObservable.Create<int>(async (_, _) =>
        {
            await Task.Yield();
            Interlocked.Increment(ref subscribed);
            return AsyncDisposable.Empty;
        });

        await using var subscription = await new[] { source, source }.Merge().SubscribeAsync();

        subscribed.ShouldBe(2);
    }
}
