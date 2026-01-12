using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class CastTest
{
    [Fact]
    public async Task Cast_ReferenceTypes_Success()
    {
        var source = AsyncObservable.Create<object>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("one", token);
                await observer.OnNextAsync("two", token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs = source.Cast<object, string>();
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.TrySetResult(r.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { "one", "two" });
    }

    [Fact]
    public async Task Cast_ValueTypes_Success()
    {
        var source = AsyncObservable.Create<object>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs = source.Cast<object,int>();
        var results = new List<int>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.TrySetResult(r.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Cast_InvalidCast_ProducesFailure()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<object>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("one", token);
                await observer.OnNextAsync(2, token); // will cause invalid cast to string
                await observer.OnNextAsync("three", token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs = source.Cast<object, string>();
        var results = new List<string>();
        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.TrySetResult(r),
            CancellationToken.None);

        var result = await completed.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBeOfType<InvalidCastException>();
        results.ShouldBe(new[] { "one" });
    }

    [Fact]
    public async Task Cast_DisposalStopsSource()
    {
        var disposed = false;
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<object>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("val", token);
                tcs.TrySetResult(0);
            });

            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var obs = source.Cast<object, string>();
        var valueTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await obs.SubscribeAsync(async (x, token) => valueTcs.TrySetResult(x), CancellationToken.None);

        await valueTcs.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
