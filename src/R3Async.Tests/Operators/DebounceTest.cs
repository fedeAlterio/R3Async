using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class DebounceTest
{
    static readonly TimeSpan DueTime = TimeSpan.FromMilliseconds(100);

    static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Debounce_EmitsOnlyAfterQuietPeriod()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Debounce(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        // Burst of values within the due time: only the last should survive.
        await subject.OnNextAsync(1, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(2, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await subject.OnNextAsync(3, CancellationToken.None);

        // Not enough silence yet.
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        lock (results) results.ShouldBeEmpty();

        // Now let the due time elapse without new values.
        timeProvider.Advance(DueTime);
        await WaitForAsync(() => { lock (results) return results.Count == 1; });

        lock (results) results.ShouldBe(new[] { 3 });
    }

    [Fact]
    public async Task Debounce_EmitsEachValueSeparatedByQuietPeriods()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();

        await using var subscription = await subject.Values.Debounce(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        timeProvider.Advance(DueTime);
        await WaitForAsync(() => { lock (results) return results.Count == 1; });

        await subject.OnNextAsync(2, CancellationToken.None);
        timeProvider.Advance(DueTime);
        await WaitForAsync(() => { lock (results) return results.Count == 2; });

        lock (results) results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Debounce_FlushesPendingValueOnCompletion()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Debounce(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(42, CancellationToken.None);
        // Complete before the due time elapses: the pending value is flushed.
        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        lock (results) results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task Debounce_ErrorDropsPendingValueAndCompletes()
    {
        var expected = new InvalidOperationException("boom");
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.Debounce(DueTime, timeProvider).SubscribeAsync(
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
    public async Task Debounce_DisposeStopsEmission()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = Subject.Create<int>();

        var results = new List<int>();

        var subscription = await subject.Values.Debounce(DueTime, timeProvider).SubscribeAsync(
            async (x, token) => { lock (results) results.Add(x); }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subscription.DisposeAsync();

        // Firing the timer after disposal must not emit anything.
        timeProvider.Advance(DueTime);
        await Task.Delay(50);

        lock (results) results.ShouldBeEmpty();
    }
}
