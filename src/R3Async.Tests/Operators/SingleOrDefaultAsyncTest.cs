using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SingleOrDefaultAsyncTest
{
    [Fact]
    public async Task SingleOrDefaultAsync_ReturnsSingleElement()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(42, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var result = await source.SingleOrDefaultAsync(default(int));
        result.ShouldBe(42);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_MoreThanOne_ThrowsInvalidOperationException()
    {
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

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.SingleOrDefaultAsync(default(int)));
    }

    [Fact]
    public async Task SingleOrDefaultAsync_NoElements_ReturnsDefaultProvided()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = await source.SingleOrDefaultAsync(-7);
        result.ShouldBe(-7);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithPredicate_ReturnsSingleMatching()
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

        var result = await source.SingleOrDefaultAsync(x => x == 2, default(int));
        result.ShouldBe(2);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithPredicate_MoreThanOneMatch_Throws()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.SingleOrDefaultAsync(x => x == 2, default(int)));
    }

    [Fact]
    public async Task SingleOrDefaultAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.SingleOrDefaultAsync(default(int)));
    }

    [Fact]
    public async Task SingleOrDefaultAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.SingleOrDefaultAsync(default(int), cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
