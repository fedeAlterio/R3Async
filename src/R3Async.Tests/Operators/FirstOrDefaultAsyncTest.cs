using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class FirstOrDefaultAsyncTest
{
    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsFirstMatching()
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

        var result = await source.FirstOrDefaultAsync(x => x % 2 == 0, -1);
        result.ShouldBe(2);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_NoMatch_ReturnsProvidedDefault()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = await source.FirstOrDefaultAsync(x => x % 2 == 0, -5);
        result.ShouldBe(-5);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_NoPredicate_ReturnsFirstElement()
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

        var result = await source.FirstOrDefaultAsync(-1);
        result.ShouldBe(5);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_NoElements_ReturnsDefaultDefaultValue()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = await source.FirstOrDefaultAsync();
        result.ShouldBe(default(int));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.FirstOrDefaultAsync());
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.FirstOrDefaultAsync(-1, cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
