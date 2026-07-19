using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class TimeoutTest
{
    static readonly TimeSpan DueTime = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task Timeout_FailsWhenNoValueArrivesInTime()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        timeProvider.Advance(DueTime);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task Timeout_ValuesResetTheWindow()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        // Each value arrives before the window expires, so no timeout occurs.
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(1, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(2, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(3, CancellationToken.None);

        completedTcs.Task.IsCompleted.ShouldBeFalse();
        lock (results) results.ShouldBe(new[] { 1, 2, 3 });

        // Now let the window expire with no value.
        timeProvider.Advance(DueTime);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task Timeout_ForwardsCompletionBeforeTimeout()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();

        // A later timer expiry must not produce anything further.
        timeProvider.Advance(DueTime);
    }

    [Fact]
    public async Task Timeout_ForwardsSourceFailure()
    {
        var expected = new InvalidOperationException("boom");
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnCompletedAsync(Result.Failure(expected));

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task Timeout_ForwardsOnErrorResumeWithoutTerminating()
    {
        var expected = new InvalidOperationException("boom");
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var errors = new List<Exception>();

        await using var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { lock (errors) errors.Add(ex); },
            async result => { },
            CancellationToken.None);

        await subject.OnErrorResumeAsync(expected, CancellationToken.None);
        await subject.OnNextAsync(1, CancellationToken.None);

        lock (errors) errors.ShouldBe(new[] { expected });
        lock (results) results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task Timeout_DisposeCancelsTimer()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subscription.DisposeAsync();

        // Firing the timer after disposal must not deliver a timeout.
        timeProvider.Advance(DueTime);

        completedTcs.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Timeout_TimeoutDisposesSourceSubscription()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Timeout(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        timeProvider.Advance(DueTime);
        await completedTcs.Task;

        // Values pushed after the timeout must not be delivered.
        await subject.OnNextAsync(1, CancellationToken.None);
        lock (results) results.ShouldBeEmpty();
    }
}
