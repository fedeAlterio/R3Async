using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ThrottleFirstTest
{
    static readonly TimeSpan DueTime = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ThrottleFirst_EmitsFirstValueAndDropsRestDuringWindow()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();

        await using var subscription = await subject.Values.ThrottleFirst(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); }, CancellationToken.None);

        // The first value of the window is emitted immediately.
        await subject.OnNextAsync(1, CancellationToken.None);
        lock (results) results.ShouldBe(new[] { 1 });

        // Values arriving inside the window are dropped.
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);
        lock (results) results.ShouldBe(new[] { 1 });

        // Once the window elapses, the next value opens a new window and is emitted.
        timeProvider.Advance(DueTime);
        await subject.OnNextAsync(4, CancellationToken.None);
        lock (results) results.ShouldBe(new[] { 1, 4 });
    }

    [Fact]
    public async Task ThrottleFirst_CompletesWithoutEmittingDroppedValues()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.ThrottleFirst(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        lock (results) results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ThrottleFirst_ErrorCompletesWithFailure()
    {
        var expected = new InvalidOperationException("boom");
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.ThrottleFirst(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(7, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Failure(expected));

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        lock (results) results.ShouldBe(new[] { 7 });
    }

    [Fact]
    public async Task ThrottleFirst_DisposeStopsEmission()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();

        var subscription = await subject.Values.ThrottleFirst(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        // Values after disposal must not be emitted, even after the window elapses.
        timeProvider.Advance(DueTime);
        await subject.OnNextAsync(2, CancellationToken.None);

        lock (results) results.ShouldBe(new[] { 1 });
    }
}
