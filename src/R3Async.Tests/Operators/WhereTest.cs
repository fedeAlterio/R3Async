using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class WhereTest
{
    [Fact]
    public async Task SyncPredicateFilterTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnNextAsync(4, token);
            await observer.OnNextAsync(5, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 2, 4 });
    }

    [Fact]
    public async Task AsyncPredicateFilterTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnNextAsync(4, token);
            await observer.OnNextAsync(5, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(async (x, token) =>
        {
            await Task.Yield();
            return x > 3;
        });
        
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 4, 5 });
    }

    [Fact]
    public async Task SyncPredicateAllPassTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(4, token);
            await observer.OnNextAsync(6, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 2, 4, 6 });
    }

    [Fact]
    public async Task SyncPredicateNonePassTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(3, token);
            await observer.OnNextAsync(5, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncPredicateCompletionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success, token);
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) => tcs.SetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await tcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task AsyncPredicateCompletionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success, token);
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(async (x, token) => x > 1);
        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) => tcs.SetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await tcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 2, 3 });
    }

    [Fact]
    public async Task SyncPredicateErrorPropagationTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(2, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task AsyncPredicateErrorPropagationTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(4, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(async (x, token) => x > 2);
        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 4 });
    }

    [Fact]
    public async Task SyncPredicateExceptionTest()
    {
        var expectedException = new InvalidOperationException("predicate failed");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x =>
        {
            if (x == 2)
                throw expectedException;
            return true;
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 3 });
    }

    [Fact]
    public async Task AsyncPredicateExceptionTest()
    {
        var expectedException = new InvalidOperationException("predicate failed");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(async (x, token) =>
        {
            await Task.Yield();
            if (x == 2)
                throw expectedException;
            return true;
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 3 });
    }

    [Fact]
    public async Task SyncPredicateDisposalTest()
    {
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var tcs = new TaskCompletionSource<int>();
        var subscription = await filtered.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncPredicateDisposalTest()
    {
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var filtered = observable.Where(async (x, token) => x % 2 == 0);
        var tcs = new TaskCompletionSource<int>();
        var subscription = await filtered.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ChainedWhereTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            for (int i = 1; i <= 10; i++)
                await observer.OnNextAsync(i, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var filtered = observable
            .Where(x => x > 3)
            .Where(x => x < 8)
            .Where(x => x % 2 == 0);

        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 4, 6 });
    }

    [Fact]
    public async Task EmptyObservableTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success, token);
            return AsyncDisposable.Empty;
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await filtered.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) => tcs.SetResult(true),
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReentranceDisposeOnFilteredValueTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await tcs.Task;
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                completedTcs.SetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var filtered = observable.Where(x => x % 2 == 0);
        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        subscription = await filtered.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);
        
        tcs.SetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 2 });
        disposed.ShouldBeTrue();
    }
}
