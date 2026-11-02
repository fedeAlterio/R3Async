using Shouldly;

namespace R3Async.Tests.Operators;

public class FusedSelectWhereTest
{
    [Fact(Timeout = 10000)]
    public async Task ConsecutiveSelectAndWhere_ProduceTheSameValuesAsSeparateOperators()
    {
        var source = new ManualSource<int>();
        var values = new List<string>();
        await using var subscription = await source
            .Select(x => x * 10)
            .Where(x => x % 20 == 0)
            .Select(x => x + 1)
            .Select(x => $"v{x}")
            .SubscribeAsync((value, _) =>
            {
                values.Add(value);
                return default;
            }, CancellationToken.None);

        for (var i = 1; i <= 4; i++)
        {
            await source.Observer!.OnNextAsync(i, CancellationToken.None);
        }

        values.ShouldBe(["v21", "v41"]);
    }

    [Fact]
    public void ConsecutiveSelectAndWhere_BecomeASingleOperatorOverTheSource()
    {
        var source = new ManualSource<int>();

        var chain = source.Select(x => x * 10).Where(x => x > 0).Select(x => x.ToString()).Where(x => x.Length > 1);

        chain.ShouldBeOfType<FusedObservable<int, string>>();
    }

    [Fact(Timeout = 10000)]
    public async Task ThrowingLambda_IsReportedAsAResumableErrorAndLaterValuesStillFlow()
    {
        var source = new ManualSource<int>();
        var values = new List<int>();
        var errors = new List<Exception>();
        await using var subscription = await source
            .Select(x => x == 2 ? throw new InvalidOperationException("boom") : x)
            .Where(x => x > 0)
            .SubscribeAsync((value, _) =>
            {
                values.Add(value);
                return default;
            }, (error, _) =>
            {
                errors.Add(error);
                return default;
            }, _ => default, CancellationToken.None);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        await source.Observer!.OnNextAsync(2, CancellationToken.None);
        await source.Observer!.OnNextAsync(3, CancellationToken.None);

        values.ShouldBe([1, 3]);
        errors.Single().Message.ShouldBe("boom");
    }

    [Fact(Timeout = 10000)]
    public async Task ExtendingAFusedObservable_LeavesItAndItsOtherBranchesUnchanged()
    {
        var source = new ManualSource<int>();
        var doubled = source.Select(x => x * 2);
        var evens = new List<int>();
        var all = new List<int>();
        var labels = new List<string>();
        await using var evenSubscription = await doubled.Where(x => x % 4 == 0).SubscribeAsync((value, _) =>
        {
            evens.Add(value);
            return default;
        }, CancellationToken.None);
        var evenObserver = source.Observer!;
        await using var allSubscription = await doubled.SubscribeAsync((value, _) =>
        {
            all.Add(value);
            return default;
        }, CancellationToken.None);
        var allObserver = source.Observer!;
        await using var labelSubscription = await doubled.Select(x => $"#{x}").SubscribeAsync((value, _) =>
        {
            labels.Add(value);
            return default;
        }, CancellationToken.None);
        var labelObserver = source.Observer!;

        foreach (var observer in new[] { evenObserver, allObserver, labelObserver })
        {
            await observer.OnNextAsync(1, CancellationToken.None);
            await observer.OnNextAsync(2, CancellationToken.None);
        }

        evens.ShouldBe([4]);
        all.ShouldBe([2, 4]);
        labels.ShouldBe(["#2", "#4"]);
    }
}
