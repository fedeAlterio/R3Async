using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ForEachAsyncTest
{
    [Fact]
    public async Task ForEachAsync_AsyncCallback_ProcessesAll()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
            });

            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var results = new List<int>();

        await source.ForEachAsync(async (x, token) =>
        {
            await Task.Yield();
            results.Add(x);
        });

        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ForEachAsync_SyncCallback_ProcessesAll()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(4, token);
                await observer.OnNextAsync(5, token);
                await observer.OnCompletedAsync(Result.Success);
            });

            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var results = new List<int>();
        await source.ForEachAsync(x => results.Add(x));
        results.ShouldBe(new[] { 4, 5 });
    }


    [Fact]
    public async Task ForEachAsync_CallbackThrows_Propagates()
    {
        var expected = new InvalidOperationException("boom");

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await source.ForEachAsync((x, token) => throw expected));
    }

    [Fact]
    public async Task ForEachAsync_Cancellation_ThrowsOperationCanceledException()
    {
        bool disposed = false;
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            return AsyncDisposable.Create(async () =>
            {
                disposed = true;
            });
        });

        using var cts = new CancellationTokenSource();
        var task = source.ForEachAsync((x, token) => default(ValueTask), cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
