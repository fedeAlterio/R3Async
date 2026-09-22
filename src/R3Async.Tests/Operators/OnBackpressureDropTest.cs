using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class OnBackpressureDropTest
{
    static async Task<AsyncObserver<T>> WaitForObserverAsync<T>(ManualSource<T> source)
    {
        while (source.Observer is null)
        {
            await Task.Yield();
        }

        return source.Observer;
    }

    [Fact(Timeout = 20000)]
    public async Task KeepsOnlyTheLatestValueWhileObserverIsBusy()
    {
        var source = new ManualSource<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();

        await using var subscription = await source.OnBackpressureDrop().SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                if (x == 1)
                {
                    entered.TrySetResult();
                    await release.Task;
                }
                else
                {
                    second.TrySetResult();
                }
            },
            CancellationToken.None);

        await (await WaitForObserverAsync(source)).OnNextAsync(1, CancellationToken.None);
        await entered.Task;

        await (await WaitForObserverAsync(source)).OnNextAsync(2, CancellationToken.None);
        await (await WaitForObserverAsync(source)).OnNextAsync(3, CancellationToken.None);

        release.SetResult();
        await second.Task;

        results.ShouldBe([1, 3]);
    }

    [Fact(Timeout = 20000)]
    public async Task PropagatesCompletion()
    {
        var source = new ManualSource<int>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await source.OnBackpressureDrop().SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await (await WaitForObserverAsync(source)).OnCompletedAsync(Result.Success);

        (await completed.Task).ShouldBeTrue();
    }
}
