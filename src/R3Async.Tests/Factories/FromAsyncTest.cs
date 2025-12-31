using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class FromAsyncTest
{
    [Fact]
    public async Task FromAsync_EmitsValueAndCompletion()
    {
        var observable = AsyncObservable.FromAsync(async (ct) =>
        {
            await Task.Yield();
            return 42;
        });

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        (await completedTcs.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task FromAsync_EmitsFailureOnException()
    {
        var expected = new InvalidOperationException("factory failed");

        var observable = AsyncObservable.FromAsync<int>(ct => throw expected);

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task FromAsync_DisposeCancelsFactory()
    {
        var tcsCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.FromAsync<int>(async (ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            }
            catch (OperationCanceledException)
            {
                await Task.Yield();
                tcsCancelled.SetResult(true);
                throw;
            }
        });

        var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

        await subscription.DisposeAsync();

        tcsCancelled.Task.IsCompleted.ShouldBeTrue();
    }
}
