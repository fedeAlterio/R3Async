using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class AnyAllAsyncTest
{
    [Fact]
    public async Task AnyAsync_ReturnsTrueIfAnyMatch()
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

        var any = await source.AnyAsync(x => x == 2);
        any.ShouldBeTrue();
    }

    [Fact]
    public async Task AnyAsync_ReturnsFalseIfNoneMatch()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var any = await source.AnyAsync(x => x == 2);
        any.ShouldBeFalse();
    }

    [Fact]
    public async Task AnyAsync_NoPredicate_ReturnsTrueIfAny()
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

        var any = await source.AnyAsync();
        any.ShouldBeTrue();
    }

    [Fact]
    public async Task AllAsync_ReturnsTrueIfAllMatch()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var all = await source.AllAsync(x => x % 2 == 0);
        all.ShouldBeTrue();
    }

    [Fact]
    public async Task AllAsync_ReturnsFalseIfAnyFail()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var all = await source.AllAsync(x => x % 2 == 0);
        all.ShouldBeFalse();
    }

    [Fact]
    public async Task AnyAllAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.AnyAsync());
        await Should.ThrowAsync<InvalidOperationException>(async () => await source.AllAsync(x => true));
    }

    [Fact]
    public async Task AnyAllAsync_Cancellation_ThrowsOperationCanceledException()
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
        var taskAny = source.AnyAsync(null, cts.Token).AsTask();
        var taskAll = source.AllAsync(x => true, cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await taskAny);
        await Should.ThrowAsync<OperationCanceledException>(async () => await taskAll);
        disposed.ShouldBeTrue();
    }
}
