using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class LastOrDefaultAsyncTest
{
    [Fact]
    public async Task LastOrDefaultAsync_ReturnsLastMatching()
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

        var result = await source.LastOrDefaultAsync(x => x % 2 == 0, -1);
        result.ShouldBe(2);
    }

    [Fact]
    public async Task LastOrDefaultAsync_NoMatch_ReturnsProvidedDefault()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = await source.LastOrDefaultAsync(x => x % 2 == 0, -5);
        result.ShouldBe(-5);
    }

    [Fact]
    public async Task LastOrDefaultAsync_NoPredicate_ReturnsLastElement()
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

        var result = await source.LastOrDefaultAsync(-1);
        result.ShouldBe(6);
    }

    [Fact]
    public async Task LastOrDefaultAsync_NoElements_ReturnsDefaultDefaultValue()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = await source.LastOrDefaultAsync();
        result.ShouldBe(default(int));
    }

    [Fact]
    public async Task LastOrDefaultAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.LastOrDefaultAsync());
    }

    [Fact]
    public async Task LastOrDefaultAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.LastOrDefaultAsync(-1, cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
