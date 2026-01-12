using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class ToEnumerableTest
{
    [Fact]
    public async Task ToEnumerable_EmitsValuesAndCompletion()
    {
        IEnumerable<int> Source()
        {
            yield return 1;
            yield return 2;
            yield return 3;
        }

        var observable = Source().ToAsyncObservable();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ToEnumerable_EmitsFailureOnException()
    {
        var expected = new InvalidOperationException("boom");

        IEnumerable<int> Source()
        {
            yield return 1;
            throw expected;
        }

        var observable = Source().ToAsyncObservable();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ToEnumerable_ReentrantDisposeDoesNotDeadlock()
    {
        var tcsBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IEnumerable<int> Source()
        {
            yield return 7;
            // block the enumerator thread until test ends (we won't set this)
            yield break;
        }

        var observable = Source().ToAsyncObservable();

        var onNextCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(async (x, token) =>
        {
            while (Volatile.Read(ref subscription) is null) await Task.Yield();
            // Dispose reentrantly from within OnNext
            await subscription.DisposeAsync();
            onNextCalled.TrySetResult();
        }, CancellationToken.None);

        // If DisposeAsync deadlocks when called reentrantly, this await will hang and the test will fail by timeout
        await onNextCalled.Task;

        (true).ShouldBeTrue();
    }
}
