using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class CombineLatestTest
{
    [Fact]
    public async Task CombineLatest_Basic()
    {
        var tcsLeft1Emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsRight1Emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsLeft3Emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var left = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                // emit first value
                await observer.OnNextAsync(1, token);
                tcsLeft1Emitted.SetResult();

                // wait until right emitted its first value so CombineLatest will produce (1,2)
                await tcsRight1Emitted.Task;

                // emit second value
                await observer.OnNextAsync(3, token);
                tcsLeft3Emitted.SetResult();

                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var right = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                // wait for left first value so we create (1,2)
                await tcsLeft1Emitted.Task;
                await observer.OnNextAsync(2, token);
                tcsRight1Emitted.SetResult();

                // wait for left second value so we create (3,4)
                await tcsLeft3Emitted.Task;
                await observer.OnNextAsync(4, token);

                await observer.OnCompletedAsync(Result.Success);
                completedTcs.SetResult(true);
            });
            return AsyncDisposable.Empty;
        });

        var combined = left.CombineLatest(right, (l, r) => (l, r));
        var results = new List<(int, int)>();

        await using var subscription = await combined.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        // wait for deterministic completion
        await completedTcs.Task;

        results.ShouldBe(new[] { (1, 2), (3, 2), (3, 4) });
    }

    [Fact]
    public async Task CombineLatest_LeftErrorPropagates()
    {
        var expected = new InvalidOperationException("left fail");
        var tcsLeft = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var left = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(expected));
                tcsLeft.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var right = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var combined = left.CombineLatest(right, (l, r) => (l, r));
        var completedTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await combined.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => { if (result.IsFailure) completedTcs.SetResult(result.Exception); },
            CancellationToken.None);

        await tcsLeft.Task;
        var ex = await completedTcs.Task;
        ex.ShouldBe(expected);
    }

    [Fact]
    public async Task CombineLatest_RightErrorResumeForwardsAndContinues()
    {
        var expected = new InvalidOperationException("right resume");
        var tcsLeft1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsRight1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var left = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcsLeft1.SetResult();
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var right = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                // wait for left first value to ensure (1,2) is produced first
                await tcsLeft1.Task;
                await observer.OnNextAsync(2, token);
                tcsRight1.SetResult();

                // report a resumable error, then continue
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
                completedTcs.SetResult(true);
            });
            return AsyncDisposable.Empty;
        });

        var combined = left.CombineLatest(right, (l, r) => (l, r));
        var results = new List<(int, int)>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await combined.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcsRight1.Task;
        var error = await errorTcs.Task;
        error.ShouldBe(expected);

        await completedTcs.Task;
        results.ShouldBe(new[] { (1, 2), (1, 4) });
    }

    [Fact]
    public async Task CombineLatest_DisposeStopsSources()
    {
        var leftDisposed = false;
        var rightDisposed = false;
        var tcsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var left = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcsStarted.SetResult();
                await Task.Yield();
            });
            return AsyncDisposable.Create(() =>
            {
                leftDisposed = true;
                return default;
            });
        });

        var right = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await Task.Yield();
            });
            return AsyncDisposable.Create(() =>
            {
                rightDisposed = true;
                return default;
            });
        });

        var combined = left.CombineLatest(right, (l, r) => (l, r));

        var subscription = await combined.SubscribeAsync(async (x, token) => { }, CancellationToken.None);
        await tcsStarted.Task;
        await subscription.DisposeAsync();

        leftDisposed.ShouldBeTrue();
        rightDisposed.ShouldBeTrue();
    }

}
