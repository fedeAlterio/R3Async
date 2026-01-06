using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class RangeTest
{
    [Fact]
    public async Task Range_Basic()
    {
        var obs = AsyncObservable.Range(3, 4); // 3,4,5,6
        var results = new List<int>();
        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r),
            CancellationToken.None);

        var result = await completed.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 3, 4, 5, 6 });
    }

    [Fact]
    public async Task Range_DisposeStopsEmission()
    {
        var obs = AsyncObservable.Range(0, 1000000);
        var results = new List<int>();

        var firstReceived = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable? subscription = null;

        subscription = await obs.SubscribeAsync(async (x, token) =>
        {
            while (Volatile.Read(ref subscription) is null) await Task.Yield();
            await Task.Yield();
            firstReceived.SetResult(x);
            results.Add(x);
            await subscription.DisposeAsync();
        }, CancellationToken.None);

        await subscription.DisposeAsync();
        results.Count.ShouldBe(1);
        firstReceived.Task.IsCompletedSuccessfully.ShouldBeTrue();
        results[0].ShouldBe(0);
        firstReceived.Task.Result.ShouldBe(0);
    }
}
