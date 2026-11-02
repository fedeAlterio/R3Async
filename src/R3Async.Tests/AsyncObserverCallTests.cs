using Shouldly;

namespace R3Async.Tests;

public class AsyncObserverCallTests
{
    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_ReusesTheLinkedTokenWhileTheCallerTokenIsTheSame()
    {
        using var caller = new CancellationTokenSource();
        var observer = new RecordingObserver();

        await observer.OnNextAsync(1, caller.Token);
        await observer.OnNextAsync(2, caller.Token);

        observer.Values.ShouldBe([1, 2]);
        observer.Tokens[0].CanBeCanceled.ShouldBeTrue();
        observer.Tokens[1].ShouldBe(observer.Tokens[0]);
        await observer.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_LinksANewTokenWhenTheCallerTokenChanges()
    {
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var observer = new RecordingObserver();

        await observer.OnNextAsync(1, first.Token);
        await observer.OnNextAsync(2, second.Token);
        second.Cancel();

        observer.Tokens[1].ShouldNotBe(observer.Tokens[0]);
        observer.Tokens[1].IsCancellationRequested.ShouldBeTrue();
        observer.Tokens[0].IsCancellationRequested.ShouldBeFalse();
        await observer.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_CallerCancellationReachesTheReusedToken()
    {
        using var caller = new CancellationTokenSource();
        var observer = new RecordingObserver();
        await observer.OnNextAsync(1, caller.Token);
        var observedCancellation = false;
        observer.OnNext = (_, token) =>
        {
            caller.Cancel();
            observedCancellation = token.IsCancellationRequested;
            return default;
        };

        await observer.OnNextAsync(2, caller.Token);

        observedCancellation.ShouldBeTrue();
        await observer.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_DisposeCancelsTheReusedToken()
    {
        using var caller = new CancellationTokenSource();
        var observer = new RecordingObserver();
        await observer.OnNextAsync(1, caller.Token);

        await observer.DisposeAsync();

        observer.Tokens[0].IsCancellationRequested.ShouldBeTrue();
        caller.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_ReentrantCallAfterAnAwaitIsDelivered()
    {
        var observer = new RecordingObserver();
        observer.OnNext = async (value, token) =>
        {
            if (value == 1)
            {
                await Task.Yield();
                await observer.OnNextAsync(2, token);
            }
        };

        await observer.OnNextAsync(1, CancellationToken.None);

        observer.Values.ShouldBe([1, 2]);
        await observer.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_ConcurrentCallFromAnotherFlowIsDropped()
    {
        var observer = new RecordingObserver();
        var firstCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.OnNext = async (value, _) =>
        {
            if (value == 1)
            {
                firstCallStarted.SetResult();
                await releaseFirstCall.Task;
            }
        };

        var firstCall = observer.OnNextAsync(1, CancellationToken.None).AsTask();
        await firstCallStarted.Task;
        await Task.Run(() => observer.OnNextAsync(2, CancellationToken.None).AsTask());
        releaseFirstCall.SetResult();
        await firstCall;

        observer.Values.ShouldBe([1]);
        await observer.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task OnNextAsync_SequentialCallsFromDifferentFlowsAreDelivered()
    {
        var observer = new RecordingObserver();

        await Task.Run(() => observer.OnNextAsync(1, CancellationToken.None).AsTask());
        await Task.Run(() => observer.OnNextAsync(2, CancellationToken.None).AsTask());

        observer.Values.ShouldBe([1, 2]);
        await observer.DisposeAsync();
    }

    sealed class RecordingObserver : AsyncObserver<int>
    {
        public List<int> Values { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Func<int, CancellationToken, ValueTask>? OnNext { get; set; }

        protected override ValueTask OnNextAsyncCore(int value, CancellationToken cancellationToken)
        {
            Values.Add(value);
            Tokens.Add(cancellationToken);
            return OnNext?.Invoke(value, cancellationToken) ?? default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken) => default;

        protected override ValueTask OnCompletedAsyncCore(Result result) => default;
    }
}
