using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class LastAsyncTest
{
    [Fact]
    public async Task LastAsync_ReturnsLastMatching()
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

        var result = await source.LastAsync(x => x % 2 == 0);
        result.ShouldBe(2);
    }

    [Fact]
    public async Task LastAsync_NoMatch_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.LastAsync(x => x % 2 == 0));
    }

    [Fact]
    public async Task LastAsync_PredicateThrows_PropagatesException()
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

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.LastAsync(x =>
        {
            if (x == 2) throw expected;
            return false;
        }));
    }

    [Fact]
    public async Task LastAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.LastAsync(x => true, cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task LastAsync_NoPredicate_ReturnsLastElement()
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

        var result = await source.LastAsync();
        result.ShouldBe(6);
    }

    [Fact]
    public async Task LastAsync_NoElements_ThrowsInvalidOperationException()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.LastAsync());
    }

    [Fact]
    public async Task LastAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.LastAsync());
    }
}
