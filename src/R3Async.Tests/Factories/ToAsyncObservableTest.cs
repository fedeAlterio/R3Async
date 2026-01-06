using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class ToAsyncObservableTest
{
    [Fact]
    public async Task ToAsyncObservable_EmitsValueAndCompletion()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = tcs.Task;

        var observable = task.ToAsyncObservable();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        tcs.SetResult(42);

        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task ToAsyncObservable_EmitsFailureOnException()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = tcs.Task;

        var observable = task.ToAsyncObservable();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        tcs.SetException(expected);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task ToAsyncObservable_ReentrantDisposeDoesNotDeadlock()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = tcs.Task.ToAsyncObservable();

        var onNextCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(async (x, token) =>
        {
            // Dispose reentrantly from within OnNext
            await subscription.DisposeAsync();
            onNextCalled.SetResult();
        }, CancellationToken.None);

        // Trigger the task completion which will cause the observable to call OnNext
        tcs.SetResult(7);

        // If DisposeAsync deadlocks when called reentrantly, this await will hang and the test will fail by timeout
        await onNextCalled.Task;
    }
}
