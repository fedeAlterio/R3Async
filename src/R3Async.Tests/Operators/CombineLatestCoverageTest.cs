using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class CombineLatestCoverageTest
{
    static AsyncObservable<long> Combine(AsyncObservable<long>[] s) => s.Length switch
    {
        2 => s[0].CombineLatest(s[1], (a, b) => a + b),
        3 => s[0].CombineLatest(s[1], s[2], (a, b, c) => a + b + c),
        4 => s[0].CombineLatest(s[1], s[2], s[3], (a, b, c, d) => a + b + c + d),
        5 => s[0].CombineLatest(s[1], s[2], s[3], s[4], (a, b, c, d, e) => a + b + c + d + e),
        6 => s[0].CombineLatest(s[1], s[2], s[3], s[4], s[5], (a, b, c, d, e, f) => a + b + c + d + e + f),
        7 => s[0].CombineLatest(s[1], s[2], s[3], s[4], s[5], s[6], (a, b, c, d, e, f, g) => a + b + c + d + e + f + g),
        8 => s[0].CombineLatest(s[1], s[2], s[3], s[4], s[5], s[6], s[7], (a, b, c, d, e, f, g, h) => a + b + c + d + e + f + g + h),
        _ => throw new ArgumentOutOfRangeException()
    };

    public static IEnumerable<object[]> Arities() =>
        Enumerable.Range(2, 7).Select(arity => new object[] { arity });

    [Theory]
    [MemberData(nameof(Arities))]
    public async Task CombineLatest_FirstSourceSubscribeThrows_Rethrows(int arity)
    {
        var throwing = new ThrowingSource<long>();
        var sources = new AsyncObservable<long>[arity];
        sources[0] = throwing;
        for (var i = 1; i < arity; i++)
            sources[i] = new ManualSource<long>();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Combine(sources).SubscribeAsync(async (x, ct) => { }, CancellationToken.None));
        thrown.ShouldBe(throwing.Exception);
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public async Task CombineLatest_LastSourceEmitsFirst_DoesNotEmitCombined(int arity)
    {
        var sources = Enumerable.Range(0, arity).Select(_ => new ManualSource<long>()).ToArray();
        var results = new List<long>();

        await using var subscription = await Combine(sources.Cast<AsyncObservable<long>>().ToArray()).SubscribeAsync(
            async (x, ct) => results.Add(x),
            CancellationToken.None);

        await sources[^1].Observer!.OnNextAsync(1, CancellationToken.None);
        results.ShouldBeEmpty();

        for (var i = 0; i < arity - 1; i++)
        {
            await sources[i].Observer!.OnNextAsync(1, CancellationToken.None);
        }

        results.ShouldBe(new[] { (long)arity });
    }

    [Fact]
    public async Task CombineLatest2_SecondSourceFails_CompletesWithFailure()
    {
        var s1 = new ManualSource<long>();
        var s2 = new ManualSource<long>();

        Result? completed = null;
        await using var subscription = await s1.CombineLatest(s2, (a, b) => a + b).SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completed = result,
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        await s2.Observer!.OnCompletedAsync(Result.Failure(expected));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);
    }
}
