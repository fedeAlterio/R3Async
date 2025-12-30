using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ScanTest
{
    [Fact]
    public async Task SyncAccumulatorSimple()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var scanned = source.Scan(0, (acc, x) => acc + x);
        var results = new List<int>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await scanned.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 1, 3, 6 });
    }

    [Fact]
    public async Task AsyncAccumulatorSimple()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var scanned = source.Scan(0, async (acc, x, token) =>
        {
            await Task.Yield();
            return acc + x;
        });

        var results = new List<int>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await scanned.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 1, 3, 7 });
    }

    [Fact]
    public async Task EmptySourceProducesNoValues()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var scanned = source.Scan(100, (acc, x) => acc + x);

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();

        await using var subscription = await scanned.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed.SetResult(result.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ErrorPropagation()
    {
        var expected = new InvalidOperationException("boom");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(5, token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnNextAsync(7, token);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var scanned = source.Scan(0, (acc, x) => acc + x);
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();

        await using var subscription = await scanned.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);

        var ex = await errorTcs.Task;
        ex.ShouldBe(expected);
        await tcs.Task;
        results.ShouldBe(new[] { 5, 12 });
    }

    [Fact]
    public async Task DisposalStopsSource()
    {
        var disposed = false;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var scanned = source.Scan(0, (acc, x) => acc + x);
        var tcsValue = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await scanned.SubscribeAsync(async (x, token) => tcsValue.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
