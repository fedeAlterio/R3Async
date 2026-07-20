using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ZipTest
{
    [Fact]
    public async Task Zip_PairsByIndex()
    {
        var left = new ManualSource<int>();
        var right = new ManualSource<int>();

        var zipped = left.Zip(right, (l, r) => (l, r));
        var results = new List<(int, int)>();
        Result? completed = null;

        await using var subscription = await zipped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        // left runs ahead; nothing pairs until right catches up
        await left.Observer!.OnNextAsync(1, CancellationToken.None);
        await left.Observer!.OnNextAsync(3, CancellationToken.None);
        results.ShouldBeEmpty();

        await right.Observer!.OnNextAsync(2, CancellationToken.None);
        results.ShouldBe(new[] { (1, 2) });

        await right.Observer!.OnNextAsync(4, CancellationToken.None);
        results.ShouldBe(new[] { (1, 2), (3, 4) });

        completed.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Zip_CompletesWhenShorterSourceEndsWithEmptyBuffer()
    {
        var left = new ManualSource<int>();
        var right = new ManualSource<int>();

        var zipped = left.Zip(right, (l, r) => (l, r));
        var results = new List<(int, int)>();
        Result? completed = null;

        await using var subscription = await zipped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        await left.Observer!.OnNextAsync(1, CancellationToken.None);
        await right.Observer!.OnNextAsync(2, CancellationToken.None);
        results.ShouldBe(new[] { (1, 2) });

        // left completes with an empty buffer: no further pairing is possible
        await left.Observer!.OnCompletedAsync(Result.Success);

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Zip_CompletionWaitsForBufferedValueToPair()
    {
        var left = new ManualSource<int>();
        var right = new ManualSource<int>();

        var zipped = left.Zip(right, (l, r) => (l, r));
        var results = new List<(int, int)>();
        Result? completed = null;

        await using var subscription = await zipped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        // left buffers a value then completes; the pair is still possible while its buffer is non-empty
        await left.Observer!.OnNextAsync(1, CancellationToken.None);
        await left.Observer!.OnCompletedAsync(Result.Success);
        completed.HasValue.ShouldBeFalse();

        await right.Observer!.OnNextAsync(2, CancellationToken.None);
        results.ShouldBe(new[] { (1, 2) });

        // now left's buffer is drained and it is done -> completes
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Zip_FailurePropagates()
    {
        var left = new ManualSource<int>();
        var right = new ManualSource<int>();

        var zipped = left.Zip(right, (l, r) => (l, r));
        Result? completed = null;

        await using var subscription = await zipped.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        await left.Observer!.OnCompletedAsync(Result.Failure(expected));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);
        right.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Zip_ErrorResumeForwards()
    {
        var left = new ManualSource<int>();
        var right = new ManualSource<int>();

        var zipped = left.Zip(right, (l, r) => (l, r));
        var errors = new List<Exception>();

        await using var subscription = await zipped.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errors.Add(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        await left.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);

        errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Zip_SubscribeFailureDisposesAlreadySubscribedSources()
    {
        var left = new ManualSource<int>();
        var throwing = new ThrowingSource<int>();

        var zipped = left.Zip(throwing, (l, r) => (l, r));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await zipped.SubscribeAsync(async (x, token) => { }, CancellationToken.None));

        thrown.ShouldBe(throwing.Exception);
        // the source that subscribed before the failure must be torn down
        left.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Zip_DisposeStopsSources()
    {
        var left = new ManualSource<int>();
        var right = new ManualSource<int>();

        var zipped = left.Zip(right, (l, r) => (l, r));

        var subscription = await zipped.SubscribeAsync(async (x, token) => { }, CancellationToken.None);
        await left.Observer!.OnNextAsync(1, CancellationToken.None);

        await subscription.DisposeAsync();

        left.Disposed.ShouldBeTrue();
        right.Disposed.ShouldBeTrue();
    }
}
