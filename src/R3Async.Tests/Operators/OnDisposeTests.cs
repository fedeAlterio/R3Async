using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class OnDisposeTests
{
    [Fact]
    public async Task OnDispose_ExecutesOnCompleted_Sync()
    {
        var executed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var finalObs = observable.OnDispose(() => executed = true);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await finalObs.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        executed.ShouldBeTrue();
    }

    [Fact]
    public async Task OnDispose_ExecutesOnCompleted_Async()
    {
        var executed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var finalObs = observable.OnDispose(async () =>
        {
            await Task.Yield();
            executed = true;
        });

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await finalObs.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        (await completed.Task).ShouldBeTrue();
        executed.ShouldBeTrue();
    }

    [Fact]
    public async Task OnDispose_ExecutesOnError()
    {
        var executed = false;
        var expected = new InvalidOperationException("fail");

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Failure(expected));
            return AsyncDisposable.Empty;
        });

        var finalObs = observable.OnDispose(() => executed = true);
        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await finalObs.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed.TrySetResult(result),
            CancellationToken.None);

        var result = await completed.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        executed.ShouldBeTrue();
    }

    [Fact]
    public async Task OnDispose_ExecutesOnDispose()
    {
        var executed = false;
        var tcsBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await tcsBlock.Task; // block until test disposes
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var finalObs = observable.OnDispose(() => executed = true);

        var valueTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await finalObs.SubscribeAsync(async (x, token) => valueTcs.TrySetResult(x), CancellationToken.None);

        await valueTcs.Task;
        await subscription.DisposeAsync();

        executed.ShouldBeTrue();
    }
}
