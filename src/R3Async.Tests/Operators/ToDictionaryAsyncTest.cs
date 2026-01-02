using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ToDictionaryAsyncTest
{

    [Fact]
    public async Task ToDictionaryAsync_DefaultElementSelector()
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

        var dict = await source.ToDictionaryAsync(x => x);
        dict.ShouldBe(new Dictionary<int,int> { {1,1}, {2,2}, {3,3} });
    }

    [Fact]
    public async Task ToDictionaryAsync_DuplicateKey_Throws()
    {
        var source = AsyncObservable.Create<string>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("a", token);
                await observer.OnNextAsync("A", token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await Should.ThrowAsync<ArgumentException>(async () => await source.ToDictionaryAsync(s => s.ToLower()));
    }

  
    [Fact]
    public async Task ToDictionaryAsync_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () => await source.ToDictionaryAsync(x => x));
    }

    [Fact]
    public async Task ToDictionaryAsync_Cancellation_ThrowsOperationCanceledException()
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
        var task = source.ToDictionaryAsync(x => x, cancellationToken: cts.Token).AsTask();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await task);
        disposed.ShouldBeTrue();
    }
}
