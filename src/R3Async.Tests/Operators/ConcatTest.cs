using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ConcatTest
{
    [Fact]
    public async Task SimpleConcatTwoObservablesTest()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task ConcatThreeObservablesTest()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable3 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs3.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnNextAsync(observable3, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await tcs3.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ConcatEmptyOuterObservableTest()
    {
        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await completedTcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConcatEmptyInnerObservableTest()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ConcatOuterErrorTest()
    {
        var expectedException = new InvalidOperationException("outer error");
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () => await observer.OnCompletedAsync(Result.Success));
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnCompletedAsync(Result.Failure(expectedException));
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var completedTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => {},
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    completedTcs.SetResult(result.Exception);
            },
            CancellationToken.None);

        var exception = await completedTcs.Task;
        exception.ShouldBe(expectedException);
    }

    [Fact]
    public async Task ConcatInnerErrorTest()
    {
        var expectedException = new InvalidOperationException("inner error");
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(expectedException));
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    completedTcs.SetResult(result.Exception);
            },
            CancellationToken.None);

        await tcs1.Task;
        var exception = await completedTcs.Task;
        exception.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ConcatOuterErrorResumeTest()
    {
        var expectedException = new InvalidOperationException("outer error");
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ConcatInnerErrorResumeTest()
    {
        var expectedException = new InvalidOperationException("inner error");
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnErrorResumeAsync(expectedException, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ConcatDisposalTest()
    {
        var disposed1 = false;
        var disposed2 = false;
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                tcs1.SetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed1 = true;
                return default;
            });
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Create(() =>
            {
                disposed2 = true;
                return default;
            });
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        
        var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await tcs1.Task;
        await subscription.DisposeAsync();
        
        disposed1.ShouldBeTrue();
        disposed2.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcatSequentialSubscriptionTest()
    {
        var subscribed1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscribed2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                subscribed1.SetResult();
                await observer.OnNextAsync(1, token);
                await complete1.Task;
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                subscribed2.SetResult();
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await subscribed1.Task;
        subscribed2.Task.IsCompleted.ShouldBeFalse();
        
        complete1.SetResult();
        await subscribed2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ConcatDelayedOuterEmissionTest()
    {
        var emitOuter1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitOuter2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitOuter1.Task;
                await observer.OnNextAsync(observable1, token);
                
                await emitOuter2.Task;
                await observer.OnNextAsync(observable2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        emitOuter1.SetResult();
        await tcs1.Task;
        results.ShouldBe(new[] { 1 });
        
        emitOuter2.SetResult();
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ConcatMultipleValuesPerInnerTest()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(4, token);
                await observer.OnNextAsync(5, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await concatenated.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public async Task ConcatReentranceDisposeTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await tcs.Task;
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                completedTcs.SetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var concatenated = outer.Concat();
        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        
        subscription = await concatenated.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);

        tcs.SetResult();
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcatWithOperatorsTest()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable1 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs1.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var observable2 = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(3, token);
                await observer.OnNextAsync(4, token);
                await observer.OnCompletedAsync(Result.Success);
                tcs2.SetResult();
            });
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(observable1, token);
            await observer.OnNextAsync(observable2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var result = outer.Concat()
            .Where(x => x > 1)
            .Select(x => x * 10);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        await using var subscription = await result.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs1.Task;
        await tcs2.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 20, 30, 40 });
    }

    [Fact]
    public async Task Concat_InnerSubscriptionThrows_ExceptionTerminatesWithFailure()
    {
        var expectedException = new InvalidOperationException("inner subscription failed");

        // This observable throws when subscribed
        var badObservable = AsyncObservable.Create<int>((observer, token) =>
        {
            throw expectedException;
        });

        var goodObservable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var outer = AsyncObservable.Create<AsyncObservable<int>>(async (observer, token) =>
        {
            await observer.OnNextAsync(badObservable, token);
            await observer.OnNextAsync(goodObservable, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var results = new List<int>();
        Exception? completedException = null;
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await outer.Concat().SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { }, // Should not be called
            async result =>
            {
                if (result.IsFailure)
                    completedException = result.Exception;
                completedTcs.SetResult();
            },
            CancellationToken.None);

        await completedTcs.Task;

        results.ShouldBeEmpty();
        completedException.ShouldBe(expectedException);
    }
}
