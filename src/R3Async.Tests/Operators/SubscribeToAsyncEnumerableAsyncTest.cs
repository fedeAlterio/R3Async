using System.Threading.Channels;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class SubscribeToAsyncEnumerableAsyncTest
{
    [Fact]
    public async Task SubscribeToAsyncEnumerableAsync_ForwardsValues()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeToAsyncEnumerableAsync(() => Channel.CreateUnbounded<int>());

        var list = new List<int>();
        await foreach (var x in subscription.Value)
        {
            list.Add(x);
        }

        list.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task SubscribeToAsyncEnumerableAsync_OnErrorCompletesWithException()
    {
        var expected = new InvalidOperationException("boom");

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, token);
                await observer.OnErrorResumeAsync(expected, token);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        await using var subscription = await source.SubscribeToAsyncEnumerableAsync(() => Channel.CreateUnbounded<int>());

        var enumerated = new List<int>();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var x in subscription.Value)
            {
                enumerated.Add(x);
            }
        });

        ex.ShouldNotBeNull();
        ex.ShouldBe(expected);
        enumerated.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task SubscribeToAsyncEnumerableAsync_CustomOnErrorResume_IsCalled()
    {
        var expected = new InvalidOperationException("boom2");
        var called = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(7, token);
                await observer.OnErrorResumeAsync(expected, token);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        var onErrorResume = new Func<Exception, CancellationToken, ValueTask>((ex, ct) =>
        {
            called.TrySetResult(ex);
            return default;
        });

        await using var subscription = await source.SubscribeToAsyncEnumerableAsync(() => Channel.CreateUnbounded<int>(), onErrorResume);

        var list = new List<int>();
        await foreach (var x in subscription.Value)
        {
            list.Add(x);
        }

        var received = await called.Task;
        received.ShouldBe(expected);
        list.ShouldBe(new[] { 7 });
    }

    [Fact]
    public async Task SubscribeToAsyncEnumerableAsync_SubscribesEagerly_BeforeEnumerationStarts()
    {
        var subject = Subject.Create<int>();

        var subscription = await subject.Values.SubscribeToAsyncEnumerableAsync(() => Channel.CreateUnbounded<int>());

        // Subscribing must happen as part of this call, not deferred until enumeration starts,
        // otherwise a value published between subscribing and enumerating would be lost.
        await subject.OnNextAsync(42, CancellationToken.None);

        await using var enumerator = subscription.Value.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.ShouldBe(42);
    }

    [Fact]
    public async Task SubscribeToAsyncEnumerableAsync_CancellingSubscribeToken_DoesNotCancelEnumeration()
    {
        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            // Values are pushed independently of the subscribe token, so cancelling it
            // afterwards only exercises whether enumeration itself observes cancellation.
            _ = Task.Run(async () =>
            {
                await observer.OnNextAsync(1, CancellationToken.None);
                await observer.OnNextAsync(2, CancellationToken.None);
                await observer.OnCompletedAsync(Result.Success);
            });
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Empty);
        });

        using var cts = new CancellationTokenSource();

        await using var subscription = await source.SubscribeToAsyncEnumerableAsync(
            () => Channel.CreateUnbounded<int>(),
            cancellationToken: cts.Token);

        // The token passed to SubscribeToAsyncEnumerableAsync only governs subscribing;
        // cancelling it afterwards must not cancel enumeration of the returned IAsyncEnumerable.
        cts.Cancel();

        var list = new List<int>();
        await foreach (var x in subscription.Value)
        {
            list.Add(x);
        }

        list.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task SubscribeToAsyncEnumerableAsync_Dispose_UnsubscribesSource()
    {
        var disposedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, token) =>
        {
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                disposedTcs.TrySetResult();
                return default;
            }));
        });

        var subscription = await source.SubscribeToAsyncEnumerableAsync(() => Channel.CreateUnbounded<int>());

        await subscription.DisposeAsync();

        await disposedTcs.Task;
    }
}
