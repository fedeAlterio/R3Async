using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SwitchTest
{
    [Fact]
    public async Task Switch_BasicSwitching()
    {
        var tcsInner1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsInner1Continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsInner2Completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var disposed1 = false;

        var inner1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcsInner1Started.SetResult();
                await tcsInner1Continue.Task; // will be cancelled/disposed when switched
                await observer.OnNextAsync(99, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed1 = true;
                return default;
            }));
        });

        var inner2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcsInner2Completed.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var tcsEmitSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var outer = AsyncObservable.Create<AsyncObservable<int>>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(inner1, token);
                await tcsEmitSecond.Task;
                await observer.OnNextAsync(inner2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await outer.Switch().SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completedTcs.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcsInner1Started.Task;
        // switch to inner2
        tcsEmitSecond.SetResult();

        await tcsInner2Completed.Task;
        (await completedTcs.Task).ShouldBeTrue();

        // inner1 should have been disposed when switching
        disposed1.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Switch_InnerErrorPropagates()
    {
        var expected = new InvalidOperationException("inner fail");

        var inner = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        Exception? observed = null;
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await outer.Switch().SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => observed = ex,
            async r => completedTcs.SetResult(r),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task Switch_OuterErrorPropagates()
    {
        var expected = new InvalidOperationException("outer fail");

        var inner = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner, token);
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await outer.Switch().SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async r => completedTcs.SetResult(r),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task Switch_DisposeStopsCurrentInner()
    {
        var tcsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;

        var inner = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcsStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                }
            });

            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var valueTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await outer.Switch().SubscribeAsync(async (x, token) => valueTcs.SetResult(x), CancellationToken.None);

        await tcsStarted.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
