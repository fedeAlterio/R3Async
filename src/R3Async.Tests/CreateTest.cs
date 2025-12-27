using Shouldly;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace R3Async.Tests;

public class CreateTest
{
    [Fact]
    public async Task SimpleCreateTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await Task.Yield();
            await observer.OnNextAsync(1, CancellationToken.None);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<int>();
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        var result = await tcs.Task;
        result.ShouldBe(1);
    }

    [Fact]
    public async Task MultipleValuesTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            return AsyncDisposable.Empty;
        });

        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        await Task.Delay(100);
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task CompletionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success, token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) => tcs.SetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await tcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ErrorCompletionTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Failure(expectedException), token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) =>
            {
                if (result.IsFailure)
                    tcs.SetResult(result.Exception);
            },
            CancellationToken.None);
        
        var exception = await tcs.Task;
        exception.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ErrorResumeTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(2, token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => tcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await tcs.Task;
        exception.ShouldBe(expectedException);
        await Task.Delay(100);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DisposalTest()
    {
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var tcs = new TaskCompletionSource<int>();
        var subscription = await observable.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task CancellationTest()
    {
        var cts = new CancellationTokenSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<int>();
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.SetResult(x), cts.Token);
        
        await cts.CancelAsync();
        var result = await tcs.Task;
        result.ShouldBe(1);
    }

    [Fact]
    public void NullSubscribeFunctionTest()
    {
        Should.Throw<ArgumentNullException>(() => AsyncObservable.Create<int>(null!));
    }

    [Fact]
    public async Task EmptyObservableTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success, token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) => tcs.SetResult(true),
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task DelayedEmissionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await Task.Yield();
            await observer.OnNextAsync(42, token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<int>();
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.SetResult(x), CancellationToken.None);
        
        var result = await tcs.Task;
        result.ShouldBe(42);
    }

    [Fact]
    public async Task OnNextExceptionRoutedToOnErrorResumeTest()
    {
        var expectedException = new InvalidOperationException("OnNext failed");
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                if (x == 1)
                    throw expectedException;
            },
            async (ex, token) => tcs.SetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await tcs.Task;
        exception.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ReentranceDisposeOnOnNextTest()
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

        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                if (x == 1)
                    await subscription!.DisposeAsync();
            },
            CancellationToken.None);
        
        tcs.SetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ReentranceDisposeOnOnErrorResumeTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await tcs.Task;
                await observer.OnNextAsync(1, token);
                await observer.OnErrorResumeAsync(expectedException, token);
                await observer.OnNextAsync(2, token);
                completedTcs.SetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => await subscription!.DisposeAsync(),
            null,
            CancellationToken.None);
        
        tcs.SetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ReentranceDisposeOnOnCompletedTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceDisposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await tcs.Task;
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Success, token);
                await observer.OnNextAsync(2, token);
                completedTcs.SetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                sourceDisposed = true;
                return default;
            });
        });

        var results = new List<int>();
        var disposed = false;
        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async (result, token) =>
            {
                await subscription!.DisposeAsync();
                disposed = true;
            },
            CancellationToken.None);
        
        tcs.SetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
        sourceDisposed.ShouldBeTrue();
    }
}
