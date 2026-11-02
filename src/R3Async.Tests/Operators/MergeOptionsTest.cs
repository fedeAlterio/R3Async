using Shouldly;

namespace R3Async.Tests.Operators;

public class MergeOptionsTest
{
    [Fact(Timeout = 10000)]
    public async Task Merge_ByDefaultForwardsTheCancellationOfASingleNotification()
    {
        var source = new ManualSource<int>();
        var observedCancellation = false;
        using var call = new CancellationTokenSource();
        await using var subscription = await new[] { source }.Merge().SubscribeAsync((_, token) =>
        {
            call.Cancel();
            observedCancellation = token.IsCancellationRequested;
            return default;
        }, CancellationToken.None);

        await source.Observer!.OnNextAsync(1, call.Token);

        observedCancellation.ShouldBeTrue();
    }

    [Fact(Timeout = 10000)]
    public async Task Merge_DetachedIgnoresTheCancellationOfASingleNotification()
    {
        var source = new ManualSource<int>();
        var observedCancellation = true;
        using var call = new CancellationTokenSource();
        await using var subscription = await new[] { source }.Merge(new MergeOptions { DetachInnerCancellation = true }).SubscribeAsync((_, token) =>
        {
            call.Cancel();
            observedCancellation = token.IsCancellationRequested;
            return default;
        }, CancellationToken.None);

        await source.Observer!.OnNextAsync(1, call.Token);

        observedCancellation.ShouldBeFalse();
    }

    [Fact(Timeout = 10000)]
    public async Task Merge_DetachedForwardsTheSameTokenForValuesOfDifferentSources()
    {
        var first = new ManualSource<int>();
        var second = new ManualSource<int>();
        var tokens = new List<CancellationToken>();
        using var firstCall = new CancellationTokenSource();
        using var secondCall = new CancellationTokenSource();
        await using var subscription = await new[] { first, second }.Merge(new MergeOptions { DetachInnerCancellation = true }).SubscribeAsync((_, token) =>
        {
            tokens.Add(token);
            return default;
        }, CancellationToken.None);

        await first.Observer!.OnNextAsync(1, firstCall.Token);
        await second.Observer!.OnNextAsync(2, secondCall.Token);
        await first.Observer!.OnNextAsync(3, firstCall.Token);

        tokens.Count.ShouldBe(3);
        tokens.Distinct().Count().ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task MergeOfObservables_DetachedIgnoresTheCancellationOfASingleNotificationButNotDisposal()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner = new ManualSource<int>();
        var tokens = new List<CancellationToken>();
        using var call = new CancellationTokenSource();
        var subscription = await outer.Merge(new MergeOptions { DetachInnerCancellation = true }).SubscribeAsync((_, token) =>
        {
            call.Cancel();
            tokens.Add(token);
            return default;
        }, CancellationToken.None);
        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        await inner.Observer!.OnNextAsync(1, call.Token);
        tokens[0].IsCancellationRequested.ShouldBeFalse();

        await subscription.DisposeAsync();
        tokens[0].IsCancellationRequested.ShouldBeTrue();
    }
}
