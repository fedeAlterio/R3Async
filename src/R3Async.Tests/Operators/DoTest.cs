using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class DoTest
{
    [Fact]
    public async Task AsyncOnNextSideEffectTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var sideEffects = new List<int>();
        var doObservable = observable.Do(async (x, token) =>
        {
            await Task.Yield();
            sideEffects.Add(x * 10);
        });

        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2, 3 });
        sideEffects.ShouldBe(new[] { 10, 20, 30 });
    }

    [Fact]
    public async Task SyncOnNextSideEffectTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var sideEffects = new List<int>();
        var doObservable = observable.Do(onNext: x => sideEffects.Add(x * 10));

        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2, 3 });
        sideEffects.ShouldBe(new[] { 10, 20, 30 });
    }

    [Fact]
    public async Task AsyncOnErrorResumeSideEffectTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(2, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var sideEffectErrors = new List<Exception>();
        var doObservable = observable.Do(
            onNext: null,
            onErrorResume: async (ex, token) =>
            {
                await Task.Yield();
                sideEffectErrors.Add(ex);
            });

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);
        
        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
        sideEffectErrors.ShouldBe(new[] { expectedException });
    }

    [Fact]
    public async Task SyncOnErrorResumeSideEffectTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(2, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var sideEffectErrors = new List<Exception>();
        var doObservable = observable.Do(
            onNext: null,
            onErrorResume: ex => sideEffectErrors.Add(ex));

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);
        
        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
        sideEffectErrors.ShouldBe(new[] { expectedException });
    }

    [Fact]
    public async Task AsyncOnCompletedSideEffectTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var completionResults = new List<bool>();
        var doObservable = observable.Do(
            onNext: null,
            onErrorResume: null,
            onCompleted: async result =>
            {
                await Task.Yield();
                completionResults.Add(result.IsSuccess);
            });

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
        completionResults.ShouldBe(new[] { true });
    }

    [Fact]
    public async Task SyncOnCompletedSideEffectTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var completionResults = new List<bool>();
        var doObservable = observable.Do(
            onNext: null,
            onErrorResume: null,
            onCompleted: result => completionResults.Add(result.IsSuccess));

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);
        
        var completed = await completedTcs.Task;
        completed.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
        completionResults.ShouldBe(new[] { true });
    }

    [Fact]
    public async Task AsyncAllCallbacksSideEffectsTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var onNextCalls = new List<int>();
        var onErrorCalls = new List<Exception>();
        var onCompletedCalls = new List<bool>();

        var doObservable = observable.Do(
            onNext: async (x, token) =>
            {
                await Task.Yield();
                onNextCalls.Add(x);
            },
            onErrorResume: async (ex, token) =>
            {
                await Task.Yield();
                onErrorCalls.Add(ex);
            },
            onCompleted: async result =>
            {
                await Task.Yield();
                onCompletedCalls.Add(result.IsSuccess);
            });

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>();
        var completedTcs = new TaskCompletionSource<bool>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);
        
        await errorTcs.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2 });
        onNextCalls.ShouldBe(new[] { 1, 2 });
        onErrorCalls.ShouldBe(new[] { expectedException });
        onCompletedCalls.ShouldBe(new[] { true });
    }

    [Fact]
    public async Task SyncAllCallbacksSideEffectsTest()
    {
        var expectedException = new InvalidOperationException("test error");
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expectedException, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var onNextCalls = new List<int>();
        var onErrorCalls = new List<Exception>();
        var onCompletedCalls = new List<bool>();

        var doObservable = observable.Do(
            onNext: x => onNextCalls.Add(x),
            onErrorResume: ex => onErrorCalls.Add(ex),
            onCompleted: result => onCompletedCalls.Add(result.IsSuccess));

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>();
        var completedTcs = new TaskCompletionSource<bool>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);
        
        await errorTcs.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1, 2 });
        onNextCalls.ShouldBe(new[] { 1, 2 });
        onErrorCalls.ShouldBe(new[] { expectedException });
        onCompletedCalls.ShouldBe(new[] { true });
    }

    [Fact]
    public async Task AsyncDoWithoutSideEffectsTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var doObservable = observable.Do(onNext: null);

        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task SyncDoWithoutSideEffectsTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var doObservable = observable.Do(onNext: (Action<int>?)null);

        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task AsyncDoDisposalTest()
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

        var sideEffects = new List<int>();
        var doObservable = observable.Do(async (x, token) => sideEffects.Add(x));

        var tcs = new TaskCompletionSource<int>();
        var subscription = await doObservable.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
        sideEffects.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task SyncDoDisposalTest()
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

        var sideEffects = new List<int>();
        var doObservable = observable.Do(onNext: x => sideEffects.Add(x));

        var tcs = new TaskCompletionSource<int>();
        var subscription = await doObservable.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
        sideEffects.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ChainedDoTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var sideEffects1 = new List<int>();
        var sideEffects2 = new List<int>();
        var sideEffects3 = new List<int>();

        var doObservable = observable
            .Do(onNext: x => sideEffects1.Add(x * 10))
            .Do(onNext: x => sideEffects2.Add(x * 100))
            .Do(onNext: x => sideEffects3.Add(x * 1000));

        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
        sideEffects1.ShouldBe(new[] { 10, 20 });
        sideEffects2.ShouldBe(new[] { 100, 200 });
        sideEffects3.ShouldBe(new[] { 1000, 2000 });
    }

    [Fact]
    public async Task DoWithSelectWhereChainTest()
    {
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            for (int i = 1; i <= 5; i++)
                await observer.OnNextAsync(i, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var sideEffects = new List<int>();
        var result = observable
            .Do(onNext: x => sideEffects.Add(x))
            .Where(x => x > 2)
            .Select(x => x * 10);

        var results = new List<int>();
        await using var subscription = await result.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBe(new[] { 30, 40, 50 });
        sideEffects.ShouldBe(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public async Task EmptyObservableTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var sideEffects = new List<int>();
        var doObservable = observable.Do(onNext: x => sideEffects.Add(x));

        var tcs = new TaskCompletionSource<bool>();
        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => tcs.TrySetResult(true),
            CancellationToken.None);
        
        await tcs.Task;
        results.ShouldBeEmpty();
        sideEffects.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReentranceDisposeOnDoCallbackTest()
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
                completedTcs.TrySetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var sideEffects = new List<int>();
        var doObservable = observable.Do(onNext: x => sideEffects.Add(x));

        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        subscription = await doObservable.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);
        
        tcs.TrySetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        sideEffects.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SyncOnNextExceptionPropagationTest()
    {
        var expectedException = new InvalidOperationException("side effect failed");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var doObservable = observable.Do(onNext: x =>
        {
            if (x == 2)
                throw expectedException;
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 3 });
    }

    [Fact]
    public async Task AsyncOnNextExceptionPropagationTest()
    {
        var expectedException = new InvalidOperationException("side effect failed");
        var tcs = new TaskCompletionSource();
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var doObservable = observable.Do(onNext: async (x, token) =>
        {
            await Task.Yield();
            if (x == 2)
                throw expectedException;
        });

        var errorTcs = new TaskCompletionSource<Exception>();
        var results = new List<int>();
        await using var subscription = await doObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);
        
        var exception = await errorTcs.Task;
        exception.ShouldBe(expectedException);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 3 });
    }
}
