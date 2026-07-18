using System.Threading.Channels;
using R3;
using R3Async.R3Interop;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.R3Interop;

public class ToAsyncObservableTest
{
    static PublishingConfiguration<int> UnboundedConfiguration(Action<Exception, ChannelWriter<int>>? onErrorResume = null)
    {
        return PublishingConfiguration.NonBlocking(
            () => Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true }),
            (value, writer) => writer.TryWrite(value),
            onErrorResume ?? ((error, writer) => { }));
    }

    [Fact]
    public async Task ToAsyncObservable_EmitsValuesAndCompletion()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(UnboundedConfiguration());

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnCompleted();

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ToAsyncObservable_PropagatesFailure()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(UnboundedConfiguration());

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        subject.OnCompleted(R3.Result.Failure(expected));

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task ToAsyncObservable_OnErrorResumeGoesToConfiguredCallback()
    {
        var subject = new R3.Subject<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = subject.ToAsyncObservable(UnboundedConfiguration((error, writer) => errorTcs.TrySetResult(error)));

        await using var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        subject.OnErrorResume(expected);

        var error = await errorTcs.Task;
        error.ShouldBe(expected);
    }

    [Fact]
    public async Task ToAsyncObservable_BlockingMode_PropagatesOnErrorResume()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(PublishingConfiguration.Blocking<int>());

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errorTcs.TrySetResult(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        subject.OnErrorResume(expected);

        var error = await errorTcs.Task;
        error.ShouldBe(expected);
    }

    [Fact]
    public async Task ToAsyncObservable_DisposeUnsubscribesFromSource()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(UnboundedConfiguration());

        var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

        await subscription.DisposeAsync();

        // R3 Subject unsubscription is synchronous on Dispose of its subscription
        subject.OnNext(1);
        subject.OnCompleted();
    }

    [Fact]
    public async Task ToAsyncObservable_SlowConsumerReceivesAllValuesInOrder()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(UnboundedConfiguration());

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                await Task.Yield();
                results.Add(x);
            },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(),
            CancellationToken.None);

        for (int i = 0; i < 100; i++)
        {
            subject.OnNext(i);
        }

        subject.OnCompleted();

        await completedTcs.Task;
        results.ShouldBe(Enumerable.Range(0, 100));
    }

    [Fact]
    public async Task ToAsyncObservable_BlockingMode_EmitsValuesAndCompletion()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(PublishingConfiguration.Blocking<int>());

        var results = new List<int>();
        Result? completed = null;

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnCompleted();

        // Blocking mode dispatches synchronously on the emitting thread: no wait needed
        results.ShouldBe(new[] { 1, 2 });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ToAsyncObservable_NonBlockingWithBoundedChannel_ReceivesAllValuesInOrder()
    {
        var subject = new R3.Subject<int>();
        var publishingConfiguration = PublishingConfiguration.NonBlocking(
            () => Channel.CreateBounded<int>(new BoundedChannelOptions(2)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            }),
            (value, writer) =>
            {
                if (!writer.TryWrite(value))
                {
                    // Block the producing thread until there is room, preserving ordering
                    writer.WriteAsync(value).AsTask().GetAwaiter().GetResult();
                }
            },
            (error, writer) => { });

        var observable = subject.ToAsyncObservable(publishingConfiguration);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                await Task.Yield();
                results.Add(x);
            },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(),
            CancellationToken.None);

        await Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                subject.OnNext(i);
            }

            subject.OnCompleted();
        });

        await completedTcs.Task;
        results.ShouldBe(Enumerable.Range(0, 50));
    }
}

public class ToObservableTest
{
    [Fact]
    public async Task ToObservable_EmitsValuesAndCompletion()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToObservable();

        var results = new List<int>();
        R3.Result? completed = null;

        using var subscription = observable.Subscribe(results.Add, _ => { }, r => completed = r);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 1, 2 });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ToObservable_PropagatesFailure()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToObservable();

        R3.Result? completed = null;
        using var subscription = observable.Subscribe(_ => { }, _ => { }, r => completed = r);

        var expected = new InvalidOperationException("boom");
        await subject.OnCompletedAsync(Result.Failure(expected));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task ToObservable_DisposeStopsNotifications()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToObservable();

        var results = new List<int>();
        var subscription = observable.Subscribe(results.Add);

        await subject.OnNextAsync(1, CancellationToken.None);
        subscription.Dispose();
        await subject.OnNextAsync(2, CancellationToken.None);

        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ToObservable_BackgroundModes_EmitValues()
    {
        var subject = Subject.Create<int>();
        var configuration = new ToObservableConfiguration
        {
            SubscribeMode = AsyncOperationMode.Background(),
            DisposeMode = AsyncOperationMode.Background()
        };

        var observable = subject.Values.ToObservable(configuration);

        var firstValueTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = observable.Subscribe(x => firstValueTcs.TrySetResult(x));

        // With background subscribe the subscription may not be established yet: retry until observed
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!firstValueTcs.Task.IsCompleted)
        {
            cts.Token.ThrowIfCancellationRequested();
            await subject.OnNextAsync(42, CancellationToken.None);
            await Task.Yield();
        }

        (await firstValueTcs.Task).ShouldBe(42);
        subscription.Dispose();
    }
}
