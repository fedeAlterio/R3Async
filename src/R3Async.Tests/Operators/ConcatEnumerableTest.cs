using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ConcatEnumerableTest
{
    [Fact]
    public async Task ConcatEnumerable_Basic()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var concat = new[] { obs1, obs2 }.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await concat.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;

        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ConcatEnumerable_Empty()
    {
        var concat = new AsyncObservable<int>[0].Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await concat.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await completedTcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConcatEnumerable_InnerError()
    {
        var expectedException = new InvalidOperationException("fail");
        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(expectedException));
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var concat = new[] { obs1, obs2 }.Concat();
        var results = new List<int>();
        Exception? completedException = null;
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await concat.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    completedException = result.Exception;
                completedTcs.TrySetResult();
            },
            CancellationToken.None);

        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        completedException.ShouldBe(expectedException);
    }

    [Fact]
    public async Task ConcatEnumerable_InnerThrowsOnSubscribe()
    {
        var expectedException = new InvalidOperationException("subscribe fail");
        var badObs = AsyncObservable.Create<int>((observer, token) =>
        {
            throw expectedException;
        });

        var goodObs = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(42, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var concat = new[] { badObs, goodObs }.Concat();
        var results = new List<int>();
        Exception? completedException = null;
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await concat.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    completedException = result.Exception;
                completedTcs.TrySetResult();
            },
            CancellationToken.None);

        await completedTcs.Task;
        results.ShouldBeEmpty();
        completedException.ShouldBe(expectedException);
    }

    [Fact]
    public async Task ConcatEnumerable_Disposal()
    {
        var disposed = false;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcs.TrySetResult();
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var concat = new[] { obs1, obs2 }.Concat();
        var results = new List<int>();

        var subscription = await concat.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await tcs.Task;
        await subscription.DisposeAsync();

        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcatEnumerable_Cancellation_BeforeFirstCompletes()
    {
        var disposed = false;
        var tcsStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcsStart.TrySetResult();
                await tcsContinue.Task; // Wait until test signals to continue
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            }));
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var concat = new[] { obs1, obs2 }.Concat();
        var results = new List<int>();
        var subscription = await concat.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);

        await tcsStart.Task; // Wait for first value
        await subscription.DisposeAsync(); // Cancel before second value
        disposed.ShouldBeTrue();
        results.ShouldBe(new[] { 1 });
    }



    [Fact]
    public async Task ConcatEnumerable_MultipleErrors_FirstOneWins()
    {
        var firstException = new InvalidOperationException("first");
        var secondException = new InvalidOperationException("second");

        var obs1 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(firstException));
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var obs2 = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Failure(secondException));
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var concat = new[] { obs1, obs2 }.Concat();
        var results = new List<int>();
        Exception? completedException = null;
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await concat.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    completedException = result.Exception;
                completedTcs.TrySetResult();
            },
            CancellationToken.None);

        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        completedException.ShouldBe(firstException);
    }

}
