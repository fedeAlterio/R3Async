using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class DistinctByTest
{
    [Fact]
    public async Task DistinctBy_SyncKey_Basic()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("a", token);
                await observer.OnNextAsync("bb", token);
                await observer.OnNextAsync("ccc", token);
                await observer.OnNextAsync("d", token);
                await observer.OnNextAsync("ee", token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var distinct = source.DistinctBy(s => s.Length);
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await distinct.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { "a", "bb", "ccc" });
    }

    [Fact]
    public async Task DistinctBy_WithComparer()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("Apple", token);
                await observer.OnNextAsync("apricot", token);
                await observer.OnNextAsync("Banana", token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        // compare by first char case-insensitive
        var comparer = new FirstCharIgnoreCaseComparer();
        var distinct = source.DistinctBy(s => s[0], comparer);
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await distinct.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { "Apple", "Banana" });
    }

    [Fact]
    public async Task DistinctUntilChangedBy_SyncKey()
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

        var sut = source.DistinctUntilChangedBy(s => char.ToLowerInvariant(s[0]));
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
    public async Task DistinctUntilChangedBy_WithComparer()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("Apple", token);
                await observer.OnNextAsync("apricot", token);
                await observer.OnNextAsync("Banana", token);
                await observer.OnNextAsync("berry", token);
                await observer.OnNextAsync("blue", token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        // compare by first char case-insensitive
        var comparer = new FirstCharIgnoreCaseComparer();
        var sut = source.DistinctUntilChangedBy(s => s[0], comparer);
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { "Apple", "Banana" });
    }

    [Fact]
    public async Task DistinctUntilChangedBy_EmptyCompletes()
    {
        var source = AsyncObservable.Create<string>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var sut = source.DistinctUntilChangedBy(s => s.Length);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<string>();

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.SetResult(r.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task DistinctUntilChangedBy_ErrorPropagation()
    {
        var expected = new InvalidOperationException("fail");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("a", token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnNextAsync("b", token);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var sut = source.DistinctUntilChangedBy(s => s[0]);
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<string>();

        await using var subscription = await sut.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);

        var ex = await errorTcs.Task;
        ex.ShouldBe(expected);
        await tcs.Task;
        results.ShouldBe(new[] { "a", "b" });
    }

    [Fact]
    public async Task DistinctUntilChangedBy_DisposalStopsSource()
    {
        var disposed = false;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("one", token);
                await observer.OnNextAsync("two", token);
                tcs.SetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var sut = source.DistinctUntilChangedBy(s => s.Length);
        var valueTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await sut.SubscribeAsync(async (x, token) => valueTcs.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }

    class FirstCharIgnoreCaseComparer : IEqualityComparer<char>
    {
        public bool Equals(char x, char y) => char.ToLowerInvariant(x) == char.ToLowerInvariant(y);
        public int GetHashCode(char obj) => char.ToLowerInvariant(obj).GetHashCode();
    }
}
