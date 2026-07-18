using System.Threading.Channels;
using R3;
using R3Async.R3Interop;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.R3Interop;

public class InteropCoverageTest
{
    static ToObservableConfiguration BlockingConfiguration { get; } = new()
    {
        SubscribeStrategy = AsyncToSyncStrategy.Blocking,
        DisposeStrategy = AsyncToSyncStrategy.Blocking
    };

    sealed class AsyncCompletingSource<T> : AsyncObservable<T>
    {
        public AsyncObserver<T>? Observer { get; private set; }
        public bool Disposed { get; private set; }
        public bool DisposeThrowsSynchronously { get; init; }

        sealed class SyncThrowingDisposable : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => throw new InvalidOperationException("dispose failed");
        }

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Observer = observer;

            if (DisposeThrowsSynchronously)
                return new SyncThrowingDisposable();

            return AsyncDisposable.Create(async () =>
            {
                await Task.Yield();
                Disposed = true;
            });
        }
    }

    [Fact]
    public void ToObservable_NullSource_Throws()
    {
        Should.Throw<ArgumentNullException>(() => ((AsyncObservable<int>)null!).ToObservable(BlockingConfiguration));
    }

    [Fact]
    public void ToObservable_Blocking_AsyncSubscribeAndAsyncDispose_Work()
    {
        var source = new AsyncCompletingSource<int>();
        var observable = source.ToObservable(BlockingConfiguration);

        var results = new List<int>();
        var subscription = observable.Subscribe(results.Add);

        source.Observer.ShouldNotBeNull();

        subscription.Dispose();
        source.Disposed.ShouldBeTrue();

        subscription.Dispose();
        source.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ToObservable_FireAndForget_SubscribeThrowsSynchronously_RoutesToOnException()
    {
        var source = new R3Async.Tests.Operators.ThrowingSource<int>();
        var exceptionTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var configuration = new ToObservableConfiguration
        {
            SubscribeStrategy = AsyncToSyncStrategy.FireAndForget(e => exceptionTcs.TrySetResult(e)),
            DisposeStrategy = AsyncToSyncStrategy.FireAndForget()
        };

        var subscription = source.ToObservable(configuration).Subscribe(_ => { });

        (await exceptionTcs.Task).ShouldBe(source.Exception);
        subscription.Dispose();
        subscription.Dispose();
    }

    [Fact]
    public async Task ToObservable_ErrorResume_ForwardsToR3Observer()
    {
        var source = new ManualSource<int>();
        var configuration = new ToObservableConfiguration
        {
            SubscribeStrategy = AsyncToSyncStrategy.Blocking,
            DisposeStrategy = AsyncToSyncStrategy.Blocking
        };

        var errors = new List<Exception>();
        using var subscription = source.ToObservable(configuration).Subscribe(
            _ => { },
            errors.Add,
            _ => { });

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task ToObservable_Blocking_SubscriptionDisposeThrows_DoesNotPropagate()
    {
        var source = new AsyncCompletingSource<int> { DisposeThrowsSynchronously = true };
        var subscription = source.ToObservable(BlockingConfiguration).Subscribe(_ => { });

        // AsyncObserver.DisposeAsync routes source-subscription dispose failures to the
        // UnhandledExceptionHandler instead of rethrowing, so Dispose completes normally.
        subscription.Dispose();
    }

    [Fact]
    public void BackpressureStrategy_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => BackpressureStrategy.FromUnboundedChannel<int>(null!));
        Should.Throw<ArgumentNullException>(() => BackpressureStrategy.FromBoundedChannel((BoundedChannelOptions)null!));
        Should.Throw<ArgumentNullException>(() => BackpressureStrategy.FromBoundedChannel<int>(null!, new BoundedChannelOptions(1)));
        Should.Throw<ArgumentNullException>(() => BackpressureStrategy.FromBoundedChannel<int>((e, w) => { }, (BoundedChannelOptions)null!));
        Should.Throw<ArgumentNullException>(() => BackpressureStrategy.FromChannel<int>(null!));
    }

    [Fact]
    public void ToAsyncObservable_NullArguments_Throw()
    {
        Observable<int> nullSource = null!;
        var source = new R3.Subject<int>();

        Should.Throw<ArgumentNullException>(() => nullSource.ToAsyncObservable(BackpressureStrategy.Blocking));
        Should.Throw<ArgumentNullException>(() => source.ToAsyncObservable((BlockingBackpressureStrategy)null!));
        Should.Throw<ArgumentNullException>(() => nullSource.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel()));
        Should.Throw<ArgumentNullException>(() => source.ToAsyncObservable((UnboundedChannelBackpressureStrategy)null!));
        Should.Throw<ArgumentNullException>(() => nullSource.ToAsyncObservable(BackpressureStrategy.FromBoundedChannel(1)));
        Should.Throw<ArgumentNullException>(() => source.ToAsyncObservable((BoundedChannelBackpressureStrategy)null!));
        Should.Throw<ArgumentNullException>(() => nullSource.ToAsyncObservable(BackpressureStrategy.FromChannel(Channel.CreateUnbounded<int>)));
        Should.Throw<ArgumentNullException>(() => source.ToAsyncObservable((ChannelBackpressureStrategy<int>)null!));
    }

    [Fact]
    public async Task ToAsyncObservable_Blocking_SlowAsyncObserver_WaitsSynchronously()
    {
        var subject = new R3.Subject<int>();
        var observable = subject.ToAsyncObservable(BackpressureStrategy.Blocking);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) =>
            {
                await Task.Yield();
                results.Add(x);
            },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await Task.Run(() =>
        {
            subject.OnNext(1);
            subject.OnCompleted();
        });

        var completed = await completedTcs.Task;
        completed.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1 });
    }
}
