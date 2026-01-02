using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class CountAsyncTest
{
    [Fact]
    public async Task CountAsync_CountAll()
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

        var result = await source.CountAsync();
        result.ShouldBe(3);
    }

    [Fact]
    public async Task CountAsync_WithPredicate()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var result = await source.CountAsync(x => x % 2 == 0);
        result.ShouldBe(2);
    }

    [Fact]
    public async Task CountAsync_EmptyReturnsZero()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = await source.CountAsync();
        result.ShouldBe(0);
    }

    [Fact]
    public async Task CountAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.CountAsync());
    }

    [Fact]
    public async Task CountAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.CountAsync(cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
