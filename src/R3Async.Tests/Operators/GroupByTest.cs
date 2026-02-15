using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class GroupByTest
{
    [Fact]
    public async Task SimpleGroupByTest()
    {
        List<int> numbers = [1, 2, 3, 4, 5, 6];

        var groups = await numbers.ToAsyncObservable()
                     .GroupBy(x => x % 2)
                     .Select(x => x.ToListAsync().AsTask().ToAsyncObservable())
                     .Merge()
                     .ToListAsync();

        groups.Count.ShouldBe(2);
        groups.Any(x => x.SequenceEqual([1, 3, 5])).ShouldBeTrue();
        groups.Any(x => x.SequenceEqual([2, 4, 6])).ShouldBeTrue();
    }

    [Fact]
    public async Task GroupByWithEmptySourceTest()
    {
        var groups = await AsyncObservable.Empty<int>()
                     .GroupBy(x => x % 2)
                     .Select(x => x.ToListAsync().AsTask().ToAsyncObservable())
                     .Merge()
                     .ToListAsync();

        groups.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SubscriptionDisposal_ShouldDispose_Groups()
    {
        List<int> numbers = [1,2,3,4,5,6];
        List<int> disposedGroups = new();
        var subscription = await numbers.ToAsyncObservable()
               .GroupBy(x => x % 2)
               .Select(x => AsyncObservable.Never<int>()
                                           .OnDispose(() => disposedGroups.Add(x.Key)))
               .Merge()
               .SubscribeAsync();

        await subscription.DisposeAsync();

        disposedGroups.Count.ShouldBe(2);
        disposedGroups.ShouldContain(0);
        disposedGroups.ShouldContain(1);
    }
}
