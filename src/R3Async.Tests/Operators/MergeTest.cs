using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class MergeTest
{
    [Fact]
    public async Task Merge_Basic()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsOuter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inner1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var inner2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(inner1, token);
                await observer.OnNextAsync(inner2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcsOuter.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;

        results.OrderBy(x => x).ShouldBe(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task Merge_InnerErrorPropagates()
    {
        var expected = new InvalidOperationException("inner fail");
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inner1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(expected));
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var inner2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner1, token);
            await observer.OnNextAsync(inner2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var results = new List<int>();
        Exception? observed = null;
        var completedTcs = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => { if (result.IsFailure) failureTcs.SetResult(result.Exception); completedTcs.SetResult(Task.CompletedTask); },
            CancellationToken.None);

        await tcs1.Task;
        var ex = await failureTcs.Task;
        ex.ShouldBe(expected);
    }

    [Fact]
    public async Task Merge_OuterErrorPropagates()
    {
        var expected = new InvalidOperationException("outer fail");

        var inner = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner, token);
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var completedTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => { if (result.IsFailure) completedTcs.SetResult(result.Exception); },
            CancellationToken.None);

        var ex = await completedTcs.Task;
        ex.ShouldBe(expected);
    }

    [Fact]
    public async Task Merge_OuterErrorResumeForwards()
    {
        var expected = new InvalidOperationException("outer resume");
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inner1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var inner2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(inner1, token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnNextAsync(inner2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        var error = await errorTcs.Task;
        error.ShouldBe(expected);
        await tcs2.Task;
        await completedTcs.Task;
        results.OrderBy(x => x).ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Merge_InnerErrorResumeForwardsAndContinues()
    {
        var expected = new InvalidOperationException("inner resume");
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inner1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var inner2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner1, token);
            await observer.OnNextAsync(inner2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        var error = await errorTcs.Task;
        error.ShouldBe(expected);
        await tcs2.Task;
        await completedTcs.Task;
        results.OrderBy(x => x).ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Merge_DisposeStopsInner()
    {
        var tcsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;

        var inner = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcsStarted.SetResult();
                await Task.Yield();
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

        var merged = outer.Merge();
        var valueTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await merged.SubscribeAsync(async (x, token) => valueTcs.SetResult(x), CancellationToken.None);

        await tcsStarted.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Merge_ReentranceDisposeOnNext()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;

        var inner = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await tcs.Task;
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                completedTcs.SetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(inner, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var results = new List<int>();
        IAsyncDisposable? subscription = null;

        subscription = await merged.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);

        tcs.SetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Merge_MultipleObservables_AllDisposedOnComplete()
    {
        const int count = 8;
        var disposed = new bool[count];
        var tcsList = new TaskCompletionSource[count];
        for (int i = 0; i < count; i++) tcsList[i] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inners = new AsyncObservable<int>[count];
        for (int i = 0; i < count; i++)
        {
            var idx = i;
            inners[i] = AsyncObservable.Create<int>(async (observer, token) =>
            {
                _ = Task.Run(async () =>
                {
                    await observer.OnNextAsync(idx, token);
                    await observer.OnCompletedAsync(Result.Success);
                    tcsList[idx].SetResult();
                });
                return AsyncDisposable.Create(() =>
                {
                    disposed[idx] = true;
                    return default;
                });
            });
        }

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                {
                    await observer.OnNextAsync(inners[i], token);
                }
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var merged = outer.Merge();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merged.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await Task.WhenAll(tcsList.Select(t => t.Task));
        await completedTcs.Task;

        for (int i = 0; i < count; i++) disposed[i].ShouldBeTrue();
    }
}
