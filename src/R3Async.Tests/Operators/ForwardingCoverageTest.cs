using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ForwardingCoverageTest
{
    sealed record Forwarded(List<int> Values, List<Exception> Errors, List<Result> Completions);

    static async Task<(Forwarded forwarded, IAsyncDisposable subscription)> SubscribeCollectingAsync(AsyncObservable<int> observable)
    {
        var forwarded = new Forwarded([], [], []);
        var subscription = await observable.SubscribeAsync(
            async (x, ct) => forwarded.Values.Add(x),
            async (ex, ct) => forwarded.Errors.Add(ex),
            async result => forwarded.Completions.Add(result),
            CancellationToken.None);
        return (forwarded, subscription);
    }

    [Fact]
    public async Task Throttle_ErrorResume_Forwards()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.ThrottleFirst(TimeSpan.FromSeconds(1)));
        await using var _ = subscription;

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Debounce_ErrorResume_Forwards()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.Debounce(TimeSpan.FromSeconds(1)));
        await using var _ = subscription;

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Switch_InnerErrorResume_Forwards()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(outer.Switch());
        await using var _ = subscription;

        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        var expected = new InvalidOperationException("inner resume");
        await inner.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Switch_OuterErrorResume_Forwards()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(outer.Switch());
        await using var _ = subscription;

        var expected = new InvalidOperationException("outer resume");
        await outer.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task MergeEnumerable_ErrorResume_Forwards()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(new AsyncObservable<int>[] { source }.Merge());
        await using var _ = subscription;

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task ConcatEnumerable_ErrorResume_Forwards()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(new AsyncObservable<int>[] { source }.Concat());
        await using var _ = subscription;

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Concat_TwoObservables_ConcatenatesValues()
    {
        var results = await AsyncObservable.Range(1, 2).Concat(AsyncObservable.Range(3, 2)).ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task TakeUntil_CancellationToken_ForwardsAllNotifications()
    {
        var source = new ManualSource<int>();
        using var cts = new CancellationTokenSource();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.TakeUntil(cts.Token));
        await using var _ = subscription;

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        forwarded.Values.ShouldBe(new[] { 1 });
        forwarded.Errors.ShouldBe(new[] { expected });
        forwarded.Completions.Count.ShouldBe(1);
        forwarded.Completions[0].IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeUntil_RawSignal_SourceForwardsAllNotifications()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.TakeUntil(notify => AsyncDisposable.Empty));
        await using var _ = subscription;

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        forwarded.Values.ShouldBe(new[] { 1 });
        forwarded.Errors.ShouldBe(new[] { expected });
        forwarded.Completions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TakeUntil_RawSignal_DisposableThrowsOnSuccess_StillCompletes()
    {
        var source = new ManualSource<int>();
        Action<Result>? stop = null;
        var observable = source.TakeUntil(notify =>
        {
            stop = notify;
            return AsyncDisposable.Create(() => throw new InvalidOperationException("dispose failed"));
        });

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await observable.SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        stop!(Result.Success);
        var completed = await completedTcs.Task;
        completed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeUntil_RawSignal_DisposableThrowsOnFailure_StillForwardsError()
    {
        var source = new ManualSource<int>();
        Action<Result>? stop = null;
        var observable = source.TakeUntil(notify =>
        {
            stop = notify;
            return AsyncDisposable.Create(() => throw new InvalidOperationException("dispose failed"));
        });

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await observable.SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => errorTcs.TrySetResult(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("signal failure");
        stop!(Result.Failure(expected));
        (await errorTcs.Task).ShouldBe(expected);
    }

    [Fact]
    public async Task TakeUntil_Task_SourceCompletes_Forwards()
    {
        var source = new ManualSource<int>();
        var tcs = new TaskCompletionSource();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.TakeUntil(tcs.Task));
        await using var _ = subscription;

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        forwarded.Values.ShouldBe(new[] { 1 });
        forwarded.Errors.ShouldBe(new[] { expected });
        forwarded.Completions.Count.ShouldBe(1);
        tcs.SetResult();
    }

    [Fact]
    public async Task TakeUntil_Other_SourceErrorResumeAndCompletion_Forward()
    {
        var source = new ManualSource<int>();
        var other = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.TakeUntil(other));
        await using var _ = subscription;

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        forwarded.Errors.ShouldBe(new[] { expected });
        forwarded.Completions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task OnErrorResumeAsFailure_ForwardsValuesAndCompletion()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.OnErrorResumeAsFailure());
        await using var _ = subscription;

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        forwarded.Values.ShouldBe(new[] { 1 });
        forwarded.Completions.Count.ShouldBe(1);
        forwarded.Completions[0].IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Share_ErrorResume_ForwardsThroughSubject()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.Share());
        await using var _ = subscription;

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        forwarded.Errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Share_NullArguments_Throw()
    {
        AsyncObservable<int> nullSource = null!;
        Should.Throw<ArgumentNullException>(() => nullSource.Share());
        Should.Throw<ArgumentNullException>(() => new ManualSource<int>().Share((Func<R3Async.Subjects.ISubject<int>>)null!));
    }

    [Fact]
    public async Task GroupBy_ErrorResume_Forwards()
    {
        var source = new ManualSource<int>();
        var errors = new List<Exception>();

        await using var subscription = await source.GroupBy(x => x % 2).SubscribeAsync(
            async (g, ct) => { },
            async (ex, ct) => errors.Add(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task Catch_SourceCompletesSuccessfully_ForwardsCompletion()
    {
        var source = new ManualSource<int>();
        var (forwarded, subscription) = await SubscribeCollectingAsync(source.Catch(ex => AsyncObservable.Return(1)));
        await using var _ = subscription;

        await source.Observer!.OnCompletedAsync(Result.Success);
        forwarded.Completions.Count.ShouldBe(1);
        forwarded.Completions[0].IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncObserver_OnNextThrowsOperationCanceled_IsSwallowed()
    {
        var source = new ManualSource<int>();
        var results = new List<int>();

        await using var subscription = await source.SubscribeAsync(
            async (x, ct) =>
            {
                if (x == 1)
                    throw new OperationCanceledException();
                results.Add(x);
            },
            CancellationToken.None);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        await source.Observer!.OnNextAsync(2, CancellationToken.None);
        results.ShouldBe(new[] { 2 });
    }
}
