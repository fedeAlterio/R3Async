using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class FirstAsyncTest
{
    [Fact]
    public async Task FirstAsync_ReturnsFirstMatching()
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

        var result = await source.FirstAsync(x => x % 2 == 0);
        result.ShouldBe(2);
    }

    [Fact]
    public async Task FirstAsync_NoMatch_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.FirstAsync(x => x % 2 == 0));
    }

    [Fact]
    public async Task FirstAsync_PredicateThrows_PropagatesException()
    {
        var expected = new InvalidOperationException("predicate failed");

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.FirstAsync(x =>
        {
            if (x == 2) throw expected;
            return false;
        }));
    }

    [Fact]
    public async Task FirstAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.FirstAsync(x => true, cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task FirstAsync_NoPredicate_ReturnsFirstElement()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(5, token);
                await observer.OnNextAsync(6, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var result = await source.FirstAsync();
        result.ShouldBe(5);
    }

    [Fact]
    public async Task FirstAsync_NoElements_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.FirstAsync());
    }

    [Fact]
    public async Task FirstAsync_NoPredicate_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.FirstAsync(cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
