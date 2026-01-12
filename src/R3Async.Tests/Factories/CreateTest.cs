using Shouldly;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace R3Async.Tests.Factories;

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

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
        var result = await tcs.Task;
        result.ShouldBe(1);
    }


    [Fact]
    public async Task CompletionTest()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => tcs.TrySetResult(result.IsSuccess),
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
            await observer.OnCompletedAsync(Result.Failure(expectedException));
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
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

        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => tcs.TrySetResult(ex),
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

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = await observable.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
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

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), cts.Token);
        
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
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => tcs.TrySetResult(true),
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

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await observable.SubscribeAsync(async (x, token) => tcs.TrySetResult(x), CancellationToken.None);
        
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

        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new List<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                if (x == 1)
                    throw expectedException;
            },
            async (ex, token) => tcs.TrySetResult(ex),
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
                completedTcs.TrySetResult();
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
        
        tcs.TrySetResult();
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
                completedTcs.TrySetResult();
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
        
        tcs.TrySetResult();
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
                await observer.OnCompletedAsync(Result.Success);
                await observer.OnNextAsync(2, token);
                completedTcs.TrySetResult();
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
            async result =>
            {
                await subscription!.DisposeAsync();
                disposed = true;
            },
            CancellationToken.None);
        
        tcs.TrySetResult();
        await completedTcs.Task;
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
        sourceDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsyncWaitsForCallbacksToCompleteTest()
    {
        var onNextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onNextCanComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onNextCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var onErrorResumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onCompletedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var emitNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitNext2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitNext.Task;
                await observer.OnNextAsync(1, token);
                
                await emitError.Task;
                await observer.OnErrorResumeAsync(new InvalidOperationException("test"), token);
                
                await emitComplete.Task;
                await observer.OnCompletedAsync(Result.Success);
                
                await emitNext2.Task;
                await observer.OnNextAsync(2, token);
            });
            return AsyncDisposable.Empty;
        });

        var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                onNextStarted.TrySetResult();
                await onNextCanComplete.Task;
                onNextCompleted.TrySetResult();
            },
            async (ex, token) =>
            {
                onErrorResumeStarted.TrySetResult();
            },
            async result =>
            {
                onCompletedStarted.TrySetResult();
            },
            CancellationToken.None);

        emitNext.TrySetResult();
        await onNextStarted.Task;
        onNextCompleted.Task.IsCompleted.ShouldBeFalse();
        
        var disposeTask = subscription.DisposeAsync().AsTask();
        await Task.Yield();
        
        disposeTask.IsCompleted.ShouldBeFalse();
        
        onNextCanComplete.TrySetResult();
        await onNextCompleted.Task;
        
        await disposeTask;
        
        emitError.TrySetResult();
        await Task.Yield();
        onErrorResumeStarted.Task.IsCompleted.ShouldBeFalse();
        
        emitComplete.TrySetResult();
        await Task.Yield();
        onCompletedStarted.Task.IsCompleted.ShouldBeFalse();
        
        emitNext2.TrySetResult();
        await Task.Yield();
        onNextStarted.Task.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsyncCompletesImmediatelyOnReentrantCallTest()
    {
        var onNextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onNextCanComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onNextCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var emitNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitNext.Task;
                await observer.OnNextAsync(1, token);
            });
            return AsyncDisposable.Empty;
        });

        IAsyncDisposable? subscription = null;
        subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                onNextStarted.TrySetResult();
                
                await subscription!.DisposeAsync();
                disposeCompleted.TrySetResult();
                
                await onNextCanComplete.Task;
                onNextCompleted.TrySetResult();
            },
            CancellationToken.None);

        emitNext.TrySetResult();
        await onNextStarted.Task;
        
        await disposeCompleted.Task;
        
        onNextCompleted.Task.IsCompleted.ShouldBeFalse();
        
        onNextCanComplete.TrySetResult();
        await onNextCompleted.Task;
    }

    [Fact]
    public async Task OnNextCancelledWhenProducerCancelsTokenTest()
    {
        var cts = new CancellationTokenSource();
        var onNextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onNextTokenCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitNext.Task;
                await observer.OnNextAsync(1, cts.Token);
            });
            return AsyncDisposable.Empty;
        });

        var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                onNextStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    onNextTokenCancelled.TrySetResult();
                }
            },
            CancellationToken.None);

        emitNext.TrySetResult();
        await onNextStarted.Task;
        
        await cts.CancelAsync();
        await onNextTokenCancelled.Task;
        
        await subscription.DisposeAsync();
    }

    [Fact]
    public async Task OnNextCancelledWhenSubscriptionDisposedTest()
    {
        var onNextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onNextTokenCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitNext.Task;
                await observer.OnNextAsync(1, token);
            });
            return AsyncDisposable.Empty;
        });

        var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                onNextStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    onNextTokenCancelled.TrySetResult();
                }
            },
            CancellationToken.None);

        emitNext.TrySetResult();
        await onNextStarted.Task;
        
        var disposeTask = subscription.DisposeAsync().AsTask();
        await onNextTokenCancelled.Task;
        
        await disposeTask;
    }

    [Fact]
    public async Task OnErrorResumeCancelledWhenProducerCancelsTokenTest()
    {
        var cts = new CancellationTokenSource();
        var onErrorResumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onErrorResumeTokenCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitError.Task;
                await observer.OnErrorResumeAsync(new InvalidOperationException("test"), cts.Token);
            });
            return AsyncDisposable.Empty;
        });

        var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) =>
            {
                onErrorResumeStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    onErrorResumeTokenCancelled.TrySetResult();
                }
            },
            null,
            CancellationToken.None);

        emitError.TrySetResult();
        await onErrorResumeStarted.Task;
        
        await cts.CancelAsync();
        await onErrorResumeTokenCancelled.Task;
        
        await subscription.DisposeAsync();
    }

    [Fact]
    public async Task OnErrorResumeCancelledWhenSubscriptionDisposedTest()
    {
        var onErrorResumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onErrorResumeTokenCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitError = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await emitError.Task;
                await observer.OnErrorResumeAsync(new InvalidOperationException("test"), token);
            });
            return AsyncDisposable.Empty;
        });

        var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) =>
            {
                onErrorResumeStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    onErrorResumeTokenCancelled.TrySetResult();
                }
            },
            null,
            CancellationToken.None);

        emitError.TrySetResult();
        await onErrorResumeStarted.Task;
        
        var disposeTask = subscription.DisposeAsync().AsTask();
        await onErrorResumeTokenCancelled.Task;
        
        await disposeTask;
    }
}
