using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class MergeCoverageTest
{
    [Fact]
    public async Task Merge_PairOverload_MergesBothSources()
    {
        var a = new ManualSource<int>();
        var b = new ManualSource<int>();
        var merged = a.Merge(b);

        var results = new List<int>();
        Result? completed = null;

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        await a.Observer!.OnNextAsync(1, CancellationToken.None);
        await b.Observer!.OnNextAsync(2, CancellationToken.None);
        await a.Observer!.OnCompletedAsync(Result.Success);
        await b.Observer!.OnNextAsync(3, CancellationToken.None);
        await b.Observer!.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 1, 2, 3 });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Merge_MaxConcurrent_MergesAllInners()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var merged = outer.Merge(maxConcurrent: 2);

        var results = new List<int>();
        Result? completed = null;

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        var inner1 = new ManualSource<int>();
        var inner2 = new ManualSource<int>();

        await outer.Observer!.OnNextAsync(inner1, CancellationToken.None);
        await outer.Observer!.OnNextAsync(inner2, CancellationToken.None);

        await inner1.Observer!.OnNextAsync(1, CancellationToken.None);
        await inner2.Observer!.OnNextAsync(2, CancellationToken.None);

        await inner1.Observer!.OnCompletedAsync(Result.Success);
        await inner2.Observer!.OnNextAsync(3, CancellationToken.None);
        await inner2.Observer!.OnCompletedAsync(Result.Success);

        await outer.Observer!.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 1, 2, 3 });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Merge_MaxConcurrent_InnerFailurePropagates()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var merged = outer.Merge(maxConcurrent: 2);

        Result? completed = null;

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        var inner = new ManualSource<int>();
        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        await inner.Observer!.OnCompletedAsync(Result.Failure(expected));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task Merge_EnumerableOverload_MergesAllSources()
    {
        var a = new ManualSource<int>();
        var b = new ManualSource<int>();
        var c = new ManualSource<int>();
        var merged = new[] { a, b, c }.Merge();

        var results = new List<int>();
        Result? completed = null;

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        await a.Observer!.OnNextAsync(1, CancellationToken.None);
        await c.Observer!.OnNextAsync(3, CancellationToken.None);
        await b.Observer!.OnNextAsync(2, CancellationToken.None);

        await a.Observer!.OnCompletedAsync(Result.Success);
        await b.Observer!.OnCompletedAsync(Result.Success);
        await c.Observer!.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 1, 3, 2 });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }
}
