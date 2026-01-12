using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class OfTypeTest
{
    [Fact]
    public async Task OfType_FiltersReferenceTypes()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<object>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync("one", token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync("three", token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.TrySetResult();
            });

            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs = source.OfType<object, string>();
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.TrySetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completed.Task).ShouldBeTrue();
        results.ShouldBe(new[] { "one", "three" });
    }

    [Fact]
    public async Task OfType_EmptyCompletes()
    {
        var source = AsyncObservable.Create<object>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var obs = source.OfType<object,string>();
        var results = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completed.TrySetResult(r.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task OfType_ErrorPropagates()
    {
        var expected = new InvalidOperationException("fail");

        var source = AsyncObservable.Create<object>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        var obs = source.OfType<object, string>();
        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async r => completed.TrySetResult(r),
            CancellationToken.None);

        var result = await completed.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task OfType_DisposalStopsSource()
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

        var obs = source.OfType<object, string>();
        var valueTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await obs.SubscribeAsync(async (x, token) => valueTcs.TrySetResult(x), CancellationToken.None);

        await valueTcs.Task;
        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
