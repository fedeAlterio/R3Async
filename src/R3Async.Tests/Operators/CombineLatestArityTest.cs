using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class CombineLatestArityTest
{
    static ManualSource<long>[] CreateSources(int arity) =>
        Enumerable.Range(0, arity).Select(_ => new ManualSource<long>()).ToArray();

    static AsyncObservable<long> Combine(ManualSource<long>[] s) => s.Length switch
    {
        3 => s[0].CombineLatest(s[1], s[2], (a, b, c) => a + b + c),
        4 => s[0].CombineLatest(s[1], s[2], s[3], (a, b, c, d) => a + b + c + d),
        5 => s[0].CombineLatest(s[1], s[2], s[3], s[4], (a, b, c, d, e) => a + b + c + d + e),
        6 => s[0].CombineLatest(s[1], s[2], s[3], s[4], s[5], (a, b, c, d, e, f) => a + b + c + d + e + f),
        7 => s[0].CombineLatest(s[1], s[2], s[3], s[4], s[5], s[6], (a, b, c, d, e, f, g) => a + b + c + d + e + f + g),
        8 => s[0].CombineLatest(s[1], s[2], s[3], s[4], s[5], s[6], s[7], (a, b, c, d, e, f, g, h) => a + b + c + d + e + f + g + h),
        _ => throw new ArgumentOutOfRangeException()
    };

    public static IEnumerable<object[]> Arities() =>
        Enumerable.Range(3, 6).Select(arity => new object[] { arity });

    public static IEnumerable<object[]> AritiesWithFailIndex() =>
        from arity in Enumerable.Range(3, 6)
        from failIndex in Enumerable.Range(0, arity)
        select new object[] { arity, failIndex };

    [Theory]
    [MemberData(nameof(Arities))]
    public async Task CombineLatest_EmitsCombinedValuesAndCompletes(int arity)
    {
        var sources = CreateSources(arity);
        var combined = Combine(sources);

        var results = new List<long>();
        Result? completed = null;

        await using var subscription = await combined.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        for (var i = 0; i < arity; i++)
        {
            results.ShouldBeEmpty();
            await sources[i].Observer!.OnNextAsync(1, CancellationToken.None);
        }

        results.ShouldBe(new[] { (long)arity });

        for (var i = 0; i < arity; i++)
        {
            await sources[i].Observer!.OnNextAsync(2, CancellationToken.None);
        }

        results.Count.ShouldBe(1 + arity);
        results[^1].ShouldBe(2L * arity);

        for (var i = 0; i < arity; i++)
        {
            completed.HasValue.ShouldBeFalse();
            await sources[i].Observer!.OnCompletedAsync(Result.Success);
        }

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(AritiesWithFailIndex))]
    public async Task CombineLatest_FailureOnAnySourcePropagatesAndDisposesAll(int arity, int failIndex)
    {
        var sources = CreateSources(arity);
        var combined = Combine(sources);

        Result? completed = null;

        await using var subscription = await combined.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        for (var i = 0; i < arity; i++)
        {
            await sources[i].Observer!.OnNextAsync(1, CancellationToken.None);
        }

        var expected = new InvalidOperationException("boom");
        await sources[failIndex].Observer!.OnCompletedAsync(Result.Failure(expected));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);

        foreach (var source in sources)
        {
            source.Disposed.ShouldBeTrue();
        }
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public async Task CombineLatest_ErrorResumeForwards(int arity)
    {
        var sources = CreateSources(arity);
        var combined = Combine(sources);

        var errors = new List<Exception>();

        await using var subscription = await combined.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errors.Add(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        await sources[arity - 1].Observer!.OnErrorResumeAsync(expected, CancellationToken.None);

        errors.ShouldBe(new[] { expected });
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public async Task CombineLatest_DisposeStopsAllSources(int arity)
    {
        var sources = CreateSources(arity);
        var combined = Combine(sources);

        Result? completed = null;

        var subscription = await combined.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        await sources[0].Observer!.OnNextAsync(1, CancellationToken.None);

        await subscription.DisposeAsync();

        foreach (var source in sources)
        {
            source.Disposed.ShouldBeTrue();
        }

        completed.HasValue.ShouldBeFalse();
    }
}
