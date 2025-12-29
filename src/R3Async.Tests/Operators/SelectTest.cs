using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SelectTest
{
    [Fact]
    public async Task SyncSelectorTransformTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(x => x * 2);
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 2, 4, 6 });
    }

    [Fact]
    public async Task AsyncSelectorTransformTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(async (x, token) =>
        {
            await Task.Yield();
            return x * 3;
        });
        
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 3, 6, 9 });
    }

    [Fact]
    public async Task SyncSelectorTypeChangeTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(x => x.ToString());
        var results = new List<string>();
        await using var subscription = await mapped.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { "1", "2", "3" });
    }

    [Fact]
    public async Task AsyncSelectorTypeChangeTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(10, token);
            await observer.OnNextAsync(20, token);
            await observer.OnNextAsync(30, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(async (x, token) =>
        {
            await Task.Yield();
            return $"Value: {x}";
        });
        
        var results = new List<string>();
        await using var subscription = await mapped.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { "Value: 10", "Value: 20", "Value: 30" });
    }

    [Fact]
    public async Task SyncSelectorCompletionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(x => x + 10);
        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result) => tcs.SetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await tcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 11, 12 });
    }

    [Fact]
    public async Task AsyncSelectorCompletionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(5, token);
            await observer.OnNextAsync(10, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(async (x, token) => x * 2);
        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result) => tcs.SetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await tcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 10, 20 });
    }

    [Fact]
    public async Task SyncSelectorErrorPropagationTest()
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

        var mapped = observable.Select(x => x * 2);
        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 2, 4 });
    }

    [Fact]
    public async Task AsyncSelectorErrorPropagationTest()
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

        var mapped = observable.Select(async (x, token) => x + 100);
        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 101, 102 });
    }

    [Fact]
    public async Task SyncSelectorExceptionTest()
    {
        var expectedException = new InvalidOperationException("selector failed");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(x =>
        {
            if (x == 2)
                throw expectedException;
            return x * 10;
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 10, 30 });
    }

    [Fact]
    public async Task AsyncSelectorExceptionTest()
    {
        var expectedException = new InvalidOperationException("selector failed");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(async (x, token) =>
        {
            await Task.Yield();
            if (x == 2)
                throw expectedException;
            return x * 10;
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 10, 30 });
    }

    [Fact]
    public async Task SyncSelectorDisposalTest()
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

        var mapped = observable.Select(x => x * 5);
        var tcs = new TaskCompletionSource<int>();
        var subscription = await mapped.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncSelectorDisposalTest()
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

        var mapped = observable.Select(async (x, token) => x * 5);
        var tcs = new TaskCompletionSource<int>();
        var subscription = await mapped.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ChainedSelectTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable
            .Select(x => x * 2)
            .Select(x => x + 10)
            .Select(x => x.ToString());

        var results = new List<string>();
        await using var subscription = await mapped.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { "12", "14", "16" });
    }

    [Fact]
    public async Task SelectWhereChainTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            for (int i = 1; i <= 5; i++)
                await observer.OnNextAsync(i, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var result = observable
            .Select(x => x * 2)
            .Where(x => x > 5)
            .Select(x => x + 1);

        var results = new List<int>();
        await using var subscription = await result.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 7, 9, 11 });
    }

    [Fact]
    public async Task EmptyObservableTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(x => x * 2);
        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result) => tcs.SetResult(true),
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReentranceDisposeOnMappedValueTest()
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

        var mapped = observable.Select(x => x * 10);
        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        subscription = await mapped.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);
        
        tcs.SetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 10 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SyncSelectorComplexTransformTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<string>(async (observer, token) =>
        {
            await observer.OnNextAsync("hello", token);
            await observer.OnNextAsync("world", token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(x => new { Text = x, Length = x.Length });
        var results = new List<(string Text, int Length)>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add((x.Text, x.Length)), 
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { ("hello", 5), ("world", 5) });
    }

    [Fact]
    public async Task AsyncSelectorComplexTransformTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.SetResult();
            return AsyncDisposable.Empty;
        });

        var mapped = observable.Select(async (x, token) =>
        {
            await Task.Yield();
            return new { Value = x, Square = x * x, Cube = x * x * x };
        });
        
        var results = new List<(int Value, int Square, int Cube)>();
        await using var subscription = await mapped.SubscribeAsync(
            async (x, token) => results.Add((x.Value, x.Square, x.Cube)), 
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { (1, 1, 1), (2, 4, 8) });
    }
}
