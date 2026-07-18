using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class TakeUntilCoverageTest
{
    [Fact]
    public async Task TakeUntil_RawSignal_SignalCompletes_CompletesSourceAndDisposesSignal()
    {
        var source = new ManualSource<int>();
        Action<Result>? stop = null;
        var stopDisposed = false;

        var observable = source.TakeUntil(notify =>
        {
            stop = notify;
            return AsyncDisposable.Create(() => { stopDisposed = true; });
        });

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);

        stop!(Result.Success);

        var completed = await completedTcs.Task;
        completed.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1 });
        stopDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeUntil_RawSignal_FailureWithSourceFailsOption_CompletesWithFailure()
    {
        var source = new ManualSource<int>();
        Action<Result>? stop = null;

        var observable = source.TakeUntil(notify =>
        {
            stop = notify;
            return AsyncDisposable.Empty;
        }, new TakeUntilOptions { SourceFailsWhenOtherFails = true });

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        stop!(Result.Failure(expected));

        var completed = await completedTcs.Task;
        completed.IsFailure.ShouldBeTrue();
        completed.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task TakeUntil_RawSignal_FailureWithDefaultOptions_ForwardsErrorResume()
    {
        var source = new ManualSource<int>();
        Action<Result>? stop = null;

        var observable = source.TakeUntil(notify =>
        {
            stop = notify;
            return AsyncDisposable.Empty;
        });

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errorTcs.TrySetResult(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        stop!(Result.Failure(expected));

        var error = await errorTcs.Task;
        error.ShouldBe(expected);
    }

    [Fact]
    public async Task TakeUntil_Other_FailureWithSourceFailsOption_CompletesWithFailure()
    {
        var source = new ManualSource<int>();
        var other = new ManualSource<int>();
        var observable = source.TakeUntil(other, new TakeUntilOptions { SourceFailsWhenOtherFails = true });

        Result? completed = null;

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        await other.Observer!.OnCompletedAsync(Result.Failure(expected));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task TakeUntil_Other_FailureWithDefaultOptions_CompletesWithSuccess()
    {
        var source = new ManualSource<int>();
        var other = new ManualSource<int>();
        var observable = source.TakeUntil(other);

        Result? completed = null;

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        await other.Observer!.OnCompletedAsync(Result.Failure(new InvalidOperationException("ignored")));

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeUntil_Other_ErrorResumeForwards()
    {
        var source = new ManualSource<int>();
        var other = new ManualSource<int>();
        var observable = source.TakeUntil(other);

        var errors = new List<Exception>();

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errors.Add(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        await other.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);

        errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task TakeUntil_Task_FaultWithSourceFailsOption_CompletesWithFailure()
    {
        var source = new ManualSource<int>();
        var taskTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = source.TakeUntil(taskTcs.Task, new TakeUntilOptions { SourceFailsWhenOtherFails = true });

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        taskTcs.SetException(expected);

        var completed = await completedTcs.Task;
        completed.IsFailure.ShouldBeTrue();
        completed.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task TakeUntil_Predicate_ErrorResumeForwards()
    {
        var source = new ManualSource<int>();
        var observable = source.TakeUntil(x => x > 10);

        var errors = new List<Exception>();

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errors.Add(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);

        errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task TakeUntil_AsyncPredicate_ErrorResumeForwards()
    {
        var source = new ManualSource<int>();
        var observable = source.TakeUntil((x, ct) => new ValueTask<bool>(x > 10));

        var errors = new List<Exception>();

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errors.Add(ex),
            async result => { },
            CancellationToken.None);

        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);

        errors.ShouldBe(new[] { expected });
    }

    [Fact]
    public async Task TakeUntil_CancellationToken_AlreadyCanceled_CompletesImmediately()
    {
        var source = new ManualSource<int>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var observable = source.TakeUntil(cts.Token);
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.IsSuccess.ShouldBeTrue();
    }
}
