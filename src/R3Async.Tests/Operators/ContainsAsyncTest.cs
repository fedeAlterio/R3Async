using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ContainsAsyncTest
{
    [Fact]
    public async Task ContainsAsync_Found_ReturnsTrue()
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

        var contains = await source.ContainsAsync(2);
        contains.ShouldBeTrue();
    }

    [Fact]
    public async Task ContainsAsync_NotFound_ReturnsFalse()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var contains = await source.ContainsAsync(2);
        contains.ShouldBeFalse();
    }

    [Fact]
    public async Task ContainsAsync_WithComparer_UsesComparer()
    {
        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("a", token);
                await observer.OnNextAsync("b", token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var contains = await source.ContainsAsync("A", StringComparer.OrdinalIgnoreCase);
        contains.ShouldBeTrue();
    }

    [Fact]
    public async Task ContainsAsync_EmptyReturnsFalse()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var contains = await source.ContainsAsync(1);
        contains.ShouldBeFalse();
    }

    [Fact]
    public async Task ContainsAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.ContainsAsync(1));
    }

    [Fact]
    public async Task ContainsAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.ContainsAsync(1, cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
