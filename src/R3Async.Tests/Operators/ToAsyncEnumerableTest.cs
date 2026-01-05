using System.Threading.Channels;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ToAsyncEnumerableTest
{
    [Fact]
    public async Task ToAsyncEnumerable_Basic()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var list = new List<int>();
        await foreach (var x in source.ToAsyncEnumerable(() => Channel.CreateUnbounded<int>()))
        {
            list.Add(x);
        }

        list.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ToAsyncEnumerable_OnErrorCompletesWithException()
    {
        var expected = new InvalidOperationException("boom");

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnErrorResumeAsync(expected, token);
            });
            return AsyncDisposable.Empty;
        });

        var enumerated = new List<int>();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var x in source.ToAsyncEnumerable(() => Channel.CreateUnbounded<int>()))
            {
                enumerated.Add(x);
            }
        });

        ex.ShouldNotBeNull();
        ex.ShouldBe(expected);
        enumerated.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ToAsyncEnumerable_CustomOnErrorResume_IsCalled()
    {
        var expected = new InvalidOperationException("boom2");
        var called = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(7, token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return AsyncDisposable.Empty;
        });

        var onErrorResume = new Func<Exception, CancellationToken, ValueTask>((ex, ct) =>
        {
            called.SetResult(ex);
            return default;
        });

        var list = new List<int>();
        await foreach (var x in source.ToAsyncEnumerable(() => Channel.CreateUnbounded<int>(), onErrorResume))
        {
            list.Add(x);
        }

        var received = await called.Task;
        received.ShouldBe(expected);
        list.ShouldBe(new[] { 7 });
    }

    [Fact]
    public async Task ToAsyncEnumerable_BreakingForeach_UnsubscribesSource()
    {
        var disposedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                // wait indefinitely until disposed; we don't use delays
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    // expected when subscription is disposed/cancelled
                }
            });

            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposedTcs.SetResult();
                return default;
            }));
        });


        var enumerated = new List<int>();
        await foreach (var x in source.ToAsyncEnumerable(() => Channel.CreateUnbounded<int>()))
        {
            enumerated.Add(x);
            break; // break early - should unsubscribe
        }

        // wait for the source disposable to be invoked
        await disposedTcs.Task;
        enumerated.ShouldBe(new[] { 1 });
    }
}
