using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class PrependTest
{
    [Fact]
    public async Task Prepend_SingleValue_Basic()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult(true);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var resultObs = source.Prepend(1);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await resultObs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completedTcs.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completedTcs.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Prepend_MultipleValues_Basic()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(10, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs.SetResult(true);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var resultObs = source.Prepend(new[] { 7, 8, 9 });
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await resultObs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completedTcs.SetResult(r.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        (await completedTcs.Task).ShouldBeTrue();
        results.ShouldBe(new[] { 7, 8, 9, 10 });
    }

    [Fact]
    public async Task Prepend_ValuesEnumerationThrows_EmitsFailure()
    {
        var expected = new InvalidOperationException("enum fail");

        IEnumerable<int> Values()
        {
            yield return 1;
            throw expected;
        }

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(99, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var obs = source.Prepend(Values());
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await obs.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async r => completedTcs.SetResult(r),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task Prepend_ReentrantDisposeDoesNotDeadlock()
    {
        var tcsBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IEnumerable<int> Values()
        {
            yield return 7;
            yield break;
        }

        var resultObs = AsyncObservable.Empty<int>().Prepend(Values());

        var onNextCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable? subscription = null;
        subscription = await resultObs.SubscribeAsync(async (x, token) =>
        {
            while (Volatile.Read(ref subscription) is null) await Task.Yield();
            await subscription.DisposeAsync();
            onNextCalled.SetResult();
        }, CancellationToken.None);

        await onNextCalled.Task;
        (true).ShouldBeTrue();
    }
}
