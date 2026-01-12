using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ThrowTest
{
    [Fact]
    public async Task Throw_EmitsFailureOnSubscribe()
    {
        var expected = new InvalidOperationException("boom");

        var obs = AsyncObservable.Throw<int>(expected);
        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();

        await using var sub = await obs.SubscribeAsync(
            async (x, token) => seen.Add(x),
            async (ex, token) => { },
            async result => completed.TrySetResult(result),
            CancellationToken.None);

        var result = await completed.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task Throw_SubscriptionDisposable_IsNoOp()
    {
        var expected = new InvalidOperationException("boom");
        var obs = AsyncObservable.Throw<int>(expected);

        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await obs.SubscribeAsync(async (x, token) => { }, async (ex, token) => { }, async result => completed.TrySetResult(result), CancellationToken.None);

        // disposing should not throw and should be safe
        await subscription.DisposeAsync();

        var result = await completed.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }
}
