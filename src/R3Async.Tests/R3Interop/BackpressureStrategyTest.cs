using System.Threading.Channels;
using R3;
using R3Async.R3Interop;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.R3Interop;

public class BackpressureStrategyTest
{
    static async Task AssertDeliversValues(AsyncObservable<int> observable)
    {
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subject = new R3.Subject<int>();
        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnCompleted();

        await completedTcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    static async Task AssertDeliversValues(Func<R3.Subject<int>, AsyncObservable<int>> factory)
    {
        var subject = new R3.Subject<int>();
        var observable = factory(subject);

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
    public async Task FromUnboundedChannel_Default_DeliversValues()
    {
        await AssertDeliversValues(subject => subject.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel()));
    }

    [Fact]
    public async Task FromUnboundedChannel_WithOptions_DeliversValues()
    {
        await AssertDeliversValues(subject => subject.ToAsyncObservable(
            BackpressureStrategy.FromUnboundedChannel(new UnboundedChannelOptions { SingleReader = true })));
    }

    [Fact]
    public async Task FromUnboundedChannel_Generic_RoutesErrorResumeToCallback()
    {
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(
            BackpressureStrategy.FromUnboundedChannel<int>((ex, writer) => errorTcs.TrySetResult(ex)));

        await using var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        subject.OnErrorResume(expected);

        (await errorTcs.Task).ShouldBe(expected);
    }

    [Fact]
    public async Task FromBoundedChannel_Capacity_DeliversValues()
    {
        await AssertDeliversValues(subject => subject.ToAsyncObservable(BackpressureStrategy.FromBoundedChannel(16)));
    }

    [Fact]
    public async Task FromBoundedChannel_Options_DeliversValues()
    {
        await AssertDeliversValues(subject => subject.ToAsyncObservable(
            BackpressureStrategy.FromBoundedChannel(new BoundedChannelOptions(16) { SingleReader = true })));
    }

    [Fact]
    public async Task FromBoundedChannel_GenericWithCapacity_RoutesErrorResumeToCallback()
    {
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(
            BackpressureStrategy.FromBoundedChannel<int>((ex, writer) => errorTcs.TrySetResult(ex), 16));

        await using var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        subject.OnErrorResume(expected);

        (await errorTcs.Task).ShouldBe(expected);
    }

    [Fact]
    public async Task FromBoundedChannel_GenericWithOptions_DeliversValues()
    {
        await AssertDeliversValues(subject => subject.ToAsyncObservable(
            BackpressureStrategy.FromBoundedChannel<int>((ex, writer) => { }, new BoundedChannelOptions(16))));
    }


    [Fact]
    public async Task ToObservable_FireAndForgetSubscribe_RoutesSubscribeExceptionToCallback()
    {
        var expected = new InvalidOperationException("subscribe failure");
        var exceptionTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = AsyncObservable.Create<int>((observer, ct) => throw expected);

        var observable = source.ToObservable(new ToObservableConfiguration
        {
            SubscribeStrategy = AsyncToSyncStrategy.FireAndForget(ex => exceptionTcs.TrySetResult(ex)),
            DisposeStrategy = AsyncToSyncStrategy.FireAndForget()
        });

        using var subscription = observable.Subscribe(_ => { });

        (await exceptionTcs.Task).ShouldBe(expected);
    }

    [Fact]
    public async Task ToObservable_BlockingSubscribe_SynchronousExceptionPropagates()
    {
        var expected = new InvalidOperationException("subscribe failure");
        var source = AsyncObservable.Create<int>((observer, ct) => throw expected);

        var observable = source.ToObservable(new ToObservableConfiguration
        {
            SubscribeStrategy = AsyncToSyncStrategy.Blocking,
            DisposeStrategy = AsyncToSyncStrategy.Blocking
        });

        var thrown = Should.Throw<InvalidOperationException>(() => observable.Subscribe(_ => { }));
        thrown.ShouldBe(expected);
    }
}
