using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class DeferTest
{
    [Fact]
    public async Task SimpleDeferTest()
    {
        var factoryCalled = false;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var deferred = AsyncObservable.Defer(async token =>
        {
            factoryCalled = true;
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(1, t);
                await observer.OnNextAsync(2, t);
                tcs.TrySetResult();
                return AsyncDisposable.Empty;
            });
        });

        factoryCalled.ShouldBeFalse();
        
        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        factoryCalled.ShouldBeTrue();
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DeferFactoryCalledOnEachSubscriptionTest()
    {
        var factoryCallCount = 0;
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var deferred = AsyncObservable.Defer(async token =>
        {
            var count = ++factoryCallCount;
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(count, t);
                if (count == 1)
                    tcs1.TrySetResult();
                else
                    tcs2.TrySetResult();
                return AsyncDisposable.Empty;
            });
        });

        var results1 = new List<int>();
        await using var subscription1 = await deferred.SubscribeAsync(async (x, token) => results1.Add(x), CancellationToken.None);
        await tcs1.Task;
        
        var results2 = new List<int>();
        await using var subscription2 = await deferred.SubscribeAsync(async (x, token) => results2.Add(x), CancellationToken.None);
        await tcs2.Task;
        
        factoryCallCount.ShouldBe(2);
        results1.ShouldBe(new[] { 1 });
        results2.ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task DeferWithCompletionTest()
    {
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(1, t);
                await observer.OnNextAsync(2, t);
                await observer.OnCompletedAsync(Result.Success);
                return AsyncDisposable.Empty;
            });
        });

        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => tcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await tcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DeferWithErrorCompletionTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(1, t);
                await observer.OnCompletedAsync(Result.Failure(expectedException));
                return AsyncDisposable.Empty;
            });
        });

        var tcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result =>
            {
                if (result.IsFailure)
                    tcs.TrySetResult(result.Exception);
            },
            CancellationToken.None);
        
        var exception = await tcs.Task;
        exception.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task DeferWithErrorResumeTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(1, t);
                await observer.OnErrorResumeAsync(expectedException, t);
                await observer.OnNextAsync(2, t);
                tcs.TrySetResult();
                return AsyncDisposable.Empty;
            });
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task DeferFactoryExceptionTest()
    {
        var expectedException = new InvalidOperationException("factory failed");
        var deferred = AsyncObservable.Defer<int>(async token =>
        {
            throw expectedException;
        });

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await deferred.SubscribeAsync(async (x, token) => { }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task DeferDisposalTest()
    {
        var disposed = false;
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(1, t);
                return AsyncDisposable.Create(() =>
                {
                    disposed = true;
                    return default;
                });
            });
        });

        var tcs = new TaskCompletionSource<int>();
        var subscription = await deferred.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeferWithOperatorsTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                for (int i = 1; i <= 5; i++)
                    await observer.OnNextAsync(i, t);
                tcs.TrySetResult();
                return AsyncDisposable.Empty;
            });
        });

        var result = deferred
            .Where(x => x > 2)
            .Select(x => x * 10);

        var results = new List<int>();
        await using var subscription = await result.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 30, 40, 50 });
    }

    [Fact]
    public async Task DeferCancellationTokenPassedToFactoryTest()
    {
        var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var deferred = AsyncObservable.Defer(async token =>
        {
            capturedToken = token;
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(1, t);
                tcs.TrySetResult();
                return AsyncDisposable.Empty;
            });
        });

        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(async (x, token) => results.Add(x), cts.Token);
        
        await tcs.Task;
        capturedToken.ShouldBe(cts.Token);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task DeferEmptyObservableTest()
    {
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnCompletedAsync(Result.Success);
                return AsyncDisposable.Empty;
            });
        });

        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => tcs.TrySetResult(true),
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeferReentranceDisposeTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        
        var deferred = AsyncObservable.Defer(async token =>
        {
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                _ = Task.Run(async () =>
                {
                    await tcs.Task;
                    await observer.OnNextAsync(1, t);
                    await observer.OnNextAsync(2, t);
                    await observer.OnNextAsync(3, t);
                    completedTcs.TrySetResult();
                });
                return AsyncDisposable.Create(() =>
                {
                    disposed = true;
                    return default;
                });
            });
        });

        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        subscription = await deferred.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);
        
        tcs.TrySetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeferDynamicObservableSelectionTest()
    {
        var useFirst = true;
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var deferred = AsyncObservable.Defer(async token =>
        {
            if (useFirst)
            {
                return AsyncObservable.Create<int>(async (observer, t) =>
                {
                    await observer.OnNextAsync(100, t);
                    tcs1.TrySetResult();
                    return AsyncDisposable.Empty;
                });
            }
            else
            {
                return AsyncObservable.Create<int>(async (observer, t) =>
                {
                    await observer.OnNextAsync(200, t);
                    tcs2.TrySetResult();
                    return AsyncDisposable.Empty;
                });
            }
        });

        var results1 = new List<int>();
        await using var subscription1 = await deferred.SubscribeAsync(async (x, token) => results1.Add(x), CancellationToken.None);
        await tcs1.Task;
        results1.ShouldBe(new[] { 100 });

        useFirst = false;
        var results2 = new List<int>();
        await using var subscription2 = await deferred.SubscribeAsync(async (x, token) => results2.Add(x), CancellationToken.None);
        await tcs2.Task;
        results2.ShouldBe(new[] { 200 });
    }

    [Fact]
    public async Task DeferAsyncFactoryTest()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = AsyncObservable.Defer(async token =>
        {
            await Task.Delay(10, token);
            return AsyncObservable.Create<int>(async (observer, t) =>
            {
                await observer.OnNextAsync(42, t);
                tcs.TrySetResult();
                return AsyncDisposable.Empty;
            });
        });

        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task ChainedDeferTest()
    {
        var outerFactoryCalled = false;
        var innerFactoryCalled = false;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var deferred = AsyncObservable.Defer(async token =>
        {
            outerFactoryCalled = true;
            return AsyncObservable.Defer(async t =>
            {
                innerFactoryCalled = true;
                return AsyncObservable.Create<int>(async (observer, ct) =>
                {
                    await observer.OnNextAsync(1, ct);
                    tcs.TrySetResult();
                    return AsyncDisposable.Empty;
                });
            });
        });

        outerFactoryCalled.ShouldBeFalse();
        innerFactoryCalled.ShouldBeFalse();

        var results = new List<int>();
        await using var subscription = await deferred.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        outerFactoryCalled.ShouldBeTrue();
        innerFactoryCalled.ShouldBeTrue();
        await tcs.Task;
        results.ShouldBe(new[] { 1 });
    }
}
