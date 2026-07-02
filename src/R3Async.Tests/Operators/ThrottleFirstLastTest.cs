using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ThrottleFirstLastTest
{
    static readonly TimeSpan DueTime = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ThrottleFirstLast_EmitsFirstValueThenLastValueOfWindow()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var itemAdded = new SemaphoreSlim(0);

        await using var subscription = await subject.Values.ThrottleFirstLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); itemAdded.Release(); }, CancellationToken.None);

        // The first value of the window is emitted immediately.
        await subject.OnNextAsync(1, CancellationToken.None);
        await itemAdded.WaitAsync();
        lock (results) results.ShouldBe(new[] { 1 });

        // Values arriving inside the window are not emitted yet; only the latest is kept.
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);
        lock (results) results.ShouldBe(new[] { 1 });

        // When the window elapses, the last pending value is emitted.
        timeProvider.Advance(DueTime);
        await itemAdded.WaitAsync();
        lock (results) results.ShouldBe(new[] { 1, 3 });

        // The window is now closed: the next value opens a new one and is emitted immediately.
        await subject.OnNextAsync(4, CancellationToken.None);
        await itemAdded.WaitAsync();
        lock (results) results.ShouldBe(new[] { 1, 3, 4 });
    }

    [Fact]
    public async Task ThrottleFirstLast_EmptyWindowDoesNotEmitOnTimerExpiry()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var itemAdded = new SemaphoreSlim(0);

        await using var subscription = await subject.Values.ThrottleFirstLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); itemAdded.Release(); }, CancellationToken.None);

        // A single value with no followers: nothing extra is emitted when the window closes.
        await subject.OnNextAsync(1, CancellationToken.None);
        await itemAdded.WaitAsync();
        timeProvider.Advance(DueTime);

        lock (results) results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ThrottleFirstLast_FlushesPendingValueOnCompletion()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.ThrottleFirstLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        // Complete before the window elapses: the pending value is flushed.
        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        lock (results) results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ThrottleFirstLast_ErrorDropsPendingValueAndCompletes()
    {
        var expected = new InvalidOperationException("boom");
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.ThrottleFirstLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(7, CancellationToken.None);
        await subject.OnNextAsync(8, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Failure(expected));

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        lock (results) results.ShouldBe(new[] { 7 });
    }

    [Fact]
    public async Task ThrottleFirstLast_DisposeStopsEmission()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();

        var subscription = await subject.Values.ThrottleFirstLast(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        // Values after disposal must not be emitted, even after the window elapses.
        timeProvider.Advance(DueTime);
        await subject.OnNextAsync(2, CancellationToken.None);

        lock (results) results.ShouldBe(new[] { 1 });
    }
}
