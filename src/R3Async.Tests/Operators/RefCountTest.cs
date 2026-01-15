using R3Async.Subjects;
using Shouldly;

namespace R3Async.Tests.Operators;

public class RefCountTest
{
    [Fact]
    public async Task FirstSubscriberConnects_SecondShares_DisposeLastDisconnects()
    {
        var source = Subject.Create<int>();
        var connectable = source.Values.Publish();

        var results1 = new List<int>();
        var results2 = new List<int>();

        var refCounted = connectable.RefCount();

        await using var sub1 = await refCounted.SubscribeAsync(async (x, token) => results1.Add(x), CancellationToken.None);
        await using var sub2 = await refCounted.SubscribeAsync(async (x, token) => results2.Add(x), CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);

        results1.ShouldBe(new[] { 1, 2 });
        results2.ShouldBe(new[] { 1, 2 });

        await sub1.DisposeAsync();
        await sub2.DisposeAsync();

        // after all subscribers disposed, further values should not be delivered
        await source.OnNextAsync(3, CancellationToken.None);

        results1.ShouldBe(new[] { 1, 2 });
        results2.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task LateSubscriberGetsOnlyFutureValues()
    {
        var source = Subject.Create<int>();
        var connectable = source.Values.Publish();
        var refCounted = connectable.RefCount();

        var results1 = new List<int>();
        var results2 = new List<int>();

        await using var sub1 = await refCounted.SubscribeAsync(async (x, token) => results1.Add(x), CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);

        await using var sub2 = await refCounted.SubscribeAsync(async (x, token) => results2.Add(x), CancellationToken.None);

        await source.OnNextAsync(2, CancellationToken.None);

        results1.ShouldBe(new[] { 1, 2 });
        results2.ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task SourceCompletionPropagatesAndDisposesConnection()
    {
        var source = Subject.Create<int>();
        var connectable = source.Values.Publish();
        var refCounted = connectable.RefCount();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sub = await refCounted.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task RefCountWithStatelessBehavior_SubscribeAfterReset_ReceivesInitialValue()
    {
        var subject = Subject.Create<int>();

        var refCounted = subject.Values.StatelessPublish(10).RefCount();

        var results1 = new List<int>();

        await using (await refCounted.SubscribeAsync(async (x, token) => results1.Add(x), CancellationToken.None))
        {
            await subject.OnNextAsync(20, CancellationToken.None);
        }

        // refcount is now 0, connection disposed; stateless behavior subject should reset to initial value
        var results2 = new List<int>();
        await using var sub2 = await refCounted.SubscribeAsync(async (x, token) => results2.Add(x), CancellationToken.None);

        results1.ShouldBe(new[] { 10, 20 });
        results2.ShouldBe(new[] { 10 });
    }
}
