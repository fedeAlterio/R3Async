using System.Reflection;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class CatchTest
{
    [Fact]
    public async Task Catch_Forwards_Handler_Values()
    {
        var expected = new InvalidOperationException("source fail");
        var innerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(expected));
            });
            return AsyncDisposable.Empty;
        });

        var handler = new Func<Exception, AsyncObservable<int>>(ex => AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
                innerDone.SetResult();
            });
            return AsyncDisposable.Empty;
        }));

        var caught = source.Catch(handler);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await caught.SubscribeAsync(async (x, token) => results.Add(x), async (ex, token) => { }, async result => completedTcs.SetResult(result), CancellationToken.None);

        await innerDone.Task;
        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Catch_InnerErrorPropagates()
    {
        var sourceFail = new InvalidOperationException("source fail");
        var handlerFail = new InvalidOperationException("handler fail");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnCompletedAsync(Result.Failure(sourceFail));
            });
            return AsyncDisposable.Empty;
        });

        var handler = new Func<Exception, AsyncObservable<int>>(ex => AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnCompletedAsync(Result.Failure(handlerFail));
            });
            return AsyncDisposable.Empty;
        }));

        var caught = source.Catch(handler);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await caught.SubscribeAsync(async (x, token) => results.Add(x), async (ex, token) => { }, async result => completedTcs.SetResult(result), CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(handlerFail);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task Catch_HandlerThrowsPropagates()
    {
        var sourceFail = new InvalidOperationException("source fail");
        var handlerThrow = new InvalidOperationException("handler throw");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnCompletedAsync(Result.Failure(sourceFail));
            });
            return AsyncDisposable.Empty;
        });

        var handler = new Func<Exception, AsyncObservable<int>>(ex => throw handlerThrow);

        var caught = source.Catch(handler);
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await caught.SubscribeAsync(async (x, token) => { }, async (ex, token) => { }, async result => completedTcs.SetResult(result), CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(handlerThrow);
    }

    [Fact]
    public async Task Catch_Dispose_DisposesHandler()
    {
        var sourceFail = new InvalidOperationException("source fail");
        var handlerSubscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerDisposed = false;

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1);
                await observer.OnCompletedAsync(Result.Failure(sourceFail));
            });
            return AsyncDisposable.Empty;
        });

        var handler = new Func<Exception, AsyncObservable<int>>(ex => AsyncObservable.Create<int>(async (observer, token) =>
        {
            handlerSubscribed.SetResult();
            return AsyncDisposable.Create(() =>
            {
                handlerDisposed = true;
                return default;
            });
        }));

        var caught = source.Catch(handler);

        var subscription = await caught.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

        await handlerSubscribed.Task; // wait until handler subscription created
        await subscription.DisposeAsync();

        handlerDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task CatchAndIgnoreErrorResume_ForwardsOnErrorResumeToUnhandled()
    {
        var expected = new InvalidOperationException("outer resume");
        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var caught = source.CatchAndIgnoreErrorResume(_ => AsyncObservable.Empty<int>());

        var field = typeof(UnhandledExceptionHandler).GetField("_unhandledException", BindingFlags.Static | BindingFlags.NonPublic);
        var previous = (Action<Exception>)field.GetValue(null)!;

        try
        {
            UnhandledExceptionHandler.Register(ex => tcs.TrySetResult(ex));

            await using var subscription = await caught.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

            var ex = await tcs.Task;
            ex.ShouldBe(expected);
        }
        finally
        {
            UnhandledExceptionHandler.Register(previous);
        }
    }
}
