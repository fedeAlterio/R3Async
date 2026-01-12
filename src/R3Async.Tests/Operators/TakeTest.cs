using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class TakeTest
{
    [Fact]
    public async Task SimpleTakeTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnNextAsync(4, token);
            await observer.OnNextAsync(5, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var taken = observable.Take(2);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await taken.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TakeZeroCompletesImmediately()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var taken = observable.Take(0);
        var completedTcs = new TaskCompletionSource<bool>();

        await using var subscription = await taken.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeMoreThanCountReturnsAll()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var taken = observable.Take(10);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>();

        await using var subscription = await taken.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ErrorPropagationTest()
    {
        var expected = new InvalidOperationException("test");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expected, token);
            await observer.OnNextAsync(2, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var taken = observable.Take(5);
        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();

        await using var subscription = await taken.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);

        var ex = await errorTcs.Task;
        ex.ShouldBe(expected);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DisposalStopsSourceTest()
    {
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var taken = observable.Take(5);
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await taken.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
