using Shouldly;
using System.Runtime.CompilerServices;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class ToAsyncEnumerableTest
{
    [Fact]
    public async Task ToAsyncEnumerable_EmitsValuesAndCompletion()
    {
        async IAsyncEnumerable<int> Source()
        {
            yield return 1;
            yield return 2;
            yield return 3;
            await Task.Yield();
        }

        var observable = Source().ToAsyncObservable();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ToAsyncEnumerable_EmitsFailureOnException()
    {
        var expected = new InvalidOperationException("boom");

        async IAsyncEnumerable<int> Source()
        {
            yield return 1;
            await Task.Yield();
            throw expected;
        }

        var observable = Source().ToAsyncObservable();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ToAsyncEnumerable_ReentrantDisposeDoesNotDeadlock()
    {
        var tcsBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<int> Source([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            // ensure we yield and allow subscriber to run
            yield return 7;
            await tcsBlock.Task; // block until test (won't set) to keep the enumerator active
        }

        var observable = Source().ToAsyncObservable();

        var onNextCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(async (x, token) =>
        {
            while (Volatile.Read(ref subscription) is null) await Task.Yield();
            // Dispose reentrantly from within OnNext
            await subscription.DisposeAsync();
            onNextCalled.SetResult();
        }, CancellationToken.None);

        // If DisposeAsync deadlocks when called reentrantly, this await will hang and the test will fail by timeout
        await onNextCalled.Task;

        (true).ShouldBeTrue();
    }
}
