using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class DistinctUntilChangedTest
{
    [Fact]
    public async Task DistinctUntilChanged_Basic()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var sut = source.DistinctUntilChanged();
        var results = new List<int>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2, 3, 1 });
    }

    [Fact]
    public async Task DistinctUntilChanged_WithComparer()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("a", token);
                await observer.OnNextAsync("A", token);
                await observer.OnNextAsync("b", token);
                await observer.OnNextAsync("B", token);
                await observer.OnNextAsync("b", token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var sut = source.DistinctUntilChanged(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { "a", "b" });
    }

    [Fact]
    public async Task DistinctUntilChanged_EmptyCompletes()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var sut = source.DistinctUntilChanged();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task DistinctUntilChanged_ErrorPropagation()
    {
        var expected = new InvalidOperationException("fail");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnNextAsync(2, token);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var sut = source.DistinctUntilChanged();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);

        var ex = await errorTcs.Task;
        ex.ShouldBe(expected);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DistinctUntilChanged_DisposalStopsSource()
    {
        var disposed = false;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var sut = source.DistinctUntilChanged();
        var valueTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await sut.SubscribeAsync(async (x, token) => valueTcs.TrySetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
