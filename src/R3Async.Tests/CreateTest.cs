namespace R3Async.Tests;

public class CreateTest
{
    [Fact]
    public async Task SimpleCreateTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await Task.Yield();
            await observer.OnNextAsync(1, CancellationToken.None);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<int>();
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        var result = await tcs.Task;
        Assert.Equal(1, result);
    }
}
