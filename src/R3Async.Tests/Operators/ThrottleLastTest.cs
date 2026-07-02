using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ThrottleLastTest
{
    static readonly TimeSpan DueTime = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ThrottleLast_EmitsOnlyLastValueOfWindowOnExpiry()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var itemAdded = new SemaphoreSlim(0);

        await using var subscription = await subject.Values.ThrottleLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); itemAdded.Release(); }, CancellationToken.None);

        // The first value opens the window but is not emitted immediately.
        await subject.OnNextAsync(1, CancellationToken.None);
        lock (results) results.ShouldBeEmpty();

        // Later values in the window replace the pending one.
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);
        lock (results) results.ShouldBeEmpty();

        // When the window elapses, only the latest value is emitted.
        timeProvider.Advance(DueTime);
        await itemAdded.WaitAsync();

        lock (results) results.ShouldBe(new[] { 3 });
    }

    [Fact]
    public async Task ThrottleLast_EmitsOneValuePerWindow()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var itemAdded = new SemaphoreSlim(0);

        await using var subscription = await subject.Values.ThrottleLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); itemAdded.Release(); }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        timeProvider.Advance(DueTime);
        await itemAdded.WaitAsync();

        await subject.OnNextAsync(2, CancellationToken.None);
        timeProvider.Advance(DueTime);
        await itemAdded.WaitAsync();

        lock (results) results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ThrottleLast_FlushesPendingValueOnCompletion()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.ThrottleLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(42, CancellationToken.None);
        // Complete before the window elapses: the pending value is flushed.
        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        lock (results) results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task ThrottleLast_ErrorDropsPendingValueAndCompletes()
    {
        var expected = new InvalidOperationException("boom");
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.ThrottleLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(7, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Failure(expected));

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        lock (results) results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThrottleLast_DisposeStopsEmission()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();

        var subscription = await subject.Values.ThrottleLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        // Firing the timer after disposal must not emit anything.
        timeProvider.Advance(DueTime);

        lock (results) results.ShouldBeEmpty();
    }
}
