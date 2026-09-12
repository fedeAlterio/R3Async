using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class GroupByUntilTest
{
    static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(100);

    static async Task Wait(SemaphoreSlim semaphore)
    {
        (await semaphore.WaitAsync(TimeSpan.FromSeconds(3))).ShouldBeTrue("the expected notification never arrived");
    }

    [Fact(Timeout = 10000)]
    public async Task GroupsByKeyLikeGroupBy()
    {
        List<int> numbers = [1, 2, 3, 4, 5, 6];

        var groups = await numbers.ToAsyncObservable()
            .GroupByUntil(x => x % 2, (_, ct) => Task.Delay(Timeout.Infinite, ct))
            .Select(x => x.ToListAsync().AsTask().ToAsyncObservable())
            .Merge()
            .ToListAsync();

        groups.Count.ShouldBe(2);
        groups.Any(x => x.SequenceEqual([1, 3, 5])).ShouldBeTrue();
        groups.Any(x => x.SequenceEqual([2, 4, 6])).ShouldBeTrue();
    }

    [Fact(Timeout = 10000)]
    public async Task EmptySourceProducesNoGroups()
    {
        var groups = await AsyncObservable.Empty<int>()
            .GroupByUntil(x => x % 2, (_, ct) => Task.Delay(Timeout.Infinite, ct))
            .Select(x => x.ToListAsync().AsTask().ToAsyncObservable())
            .Merge()
            .ToListAsync();

        groups.Count.ShouldBe(0);
    }

    [Fact(Timeout = 10000)]
    public async Task DurationCompletesTheGroup()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var completed = new List<int>();
        var groupCompleted = new SemaphoreSlim(0);

        await using var subscription = await subject.Values
            .GroupByUntil(x => x % 2, (_, ct) => Task.Delay(Idle, timeProvider, ct))
            .SubscribeAsync(
                async (group, token) =>
                {
                    var key = group.Key;
                    _ = await group.SubscribeAsync(
                        onNextAsync: async (_, _) => { },
                        onErrorResume: null,
                        onCompleted: _ => { lock (completed) completed.Add(key); groupCompleted.Release(); },
                        cancellationToken: token);
                },
                CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        lock (completed) completed.ShouldBeEmpty();

        timeProvider.Advance(Idle);
        await Wait(groupCompleted);

        lock (completed) completed.ShouldBe(new[] { 1 });
    }

    [Fact(Timeout = 10000)]
    public async Task SameKeyAfterDurationStartsANewGroup()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var keys = new List<int>();

        await using var subscription = await subject.Values
            .GroupByUntil(x => x, async (x, ct) => await x.Debounce(Idle, timeProvider).FirstAsync(ct))
            .SubscribeAsync(async (group, token) =>
            {
                keys.Add(group.Key);
            }, CancellationToken.None);

        await subject.OnNextAsync(7, CancellationToken.None);
        keys.Count.ShouldBe(1);

        timeProvider.Advance(Idle*2);
        do
        {
            await subject.OnNextAsync(7, CancellationToken.None);
            await Task.Yield();
        } while (keys.Count == 1);
    }

    [Fact(Timeout = 10000)]
    public async Task ValuesKeepReachingTheGroupBeforeTheDurationElapses()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var received = new List<int>();
        var itemAdded = new SemaphoreSlim(0);

        await using var subscription = await subject.Values
            .GroupByUntil(x => x % 2, (_, ct) => Task.Delay(Idle, timeProvider, ct))
            .SubscribeAsync(
                async (group, token) =>
                {
                    _ = await group.SubscribeAsync(async (value, _) => { lock (received) received.Add(value); itemAdded.Release(); }, token);
                },
                CancellationToken.None);

        await subject.OnNextAsync(2, CancellationToken.None);
        await Wait(itemAdded);

        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(4, CancellationToken.None);
        await Wait(itemAdded);

        lock (received) received.ShouldBe(new[] { 2, 4 });
    }

    [Fact(Timeout = 10000)]
    public async Task SubscriptionDisposalDisposesGroups()
    {
        List<int> numbers = [1, 2, 3, 4, 5, 6];
        List<int> disposedGroups = [];

        var subscription = await numbers.ToAsyncObservable()
            .GroupByUntil(x => x % 2, (_, ct) => Task.Delay(Timeout.Infinite, ct))
            .Select(x => AsyncObservable.Never<int>().OnDispose(() => disposedGroups.Add(x.Key)))
            .Merge()
            .SubscribeAsync();

        await Task.Delay(2000);
        await subscription.DisposeAsync();

        disposedGroups.Count.ShouldBe(2);
    }

}
