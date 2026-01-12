using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class MergeEnumerableTest
{
    [Fact]
    public async Task MergeEnumerable_Basic()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var merge = new[] { obs1, obs2 }.Merge();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merge.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;

        results.OrderBy(x => x).ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task MergeEnumerable_Empty()
    {
        var merge = new AsyncObservable<int>[0].Merge();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await merge.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await completedTcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task MergeEnumerable_InnerError()
    {
        var expectedException = new InvalidOperationException("fail");
        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(expectedException));
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var merge = new[] { obs1, obs2 }.Merge();
        var results = new List<int>();
        Exception? completedException = null;
        var completedTcs = new TaskCompletionSource();

        await using var subscription = await merge.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    completedException = result.Exception;
                completedTcs.TrySetResult();
            },
            CancellationToken.None);

        await completedTcs.Task;
        completedException.ShouldBe(expectedException);
    }

    [Fact]
    public async Task MergeEnumerable_Disposal()
    {
        var disposed = false;
        var tcs = new TaskCompletionSource();

        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcs.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var merge = new[] { obs1, obs2 }.Merge();
        var results = new List<int>();

        var subscription = await merge.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await tcs.Task;
        await subscription.DisposeAsync();

        disposed.ShouldBeTrue();
    }
}
