using R3Async.Subjects;
using Shouldly;

namespace R3Async.Tests.Operators;

public class TakeUntilTest
{
    [Fact]
    public async Task TakeUntil_OtherEmitsFirst_CompletesSource()
    {
        var source = Subject.Create<int>();
        var other = Subject.Create<string>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(other.Values);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);

        await other.OnNextAsync("stop", CancellationToken.None);

        await source.OnNextAsync(3, CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TakeUntil_SourceCompletesBeforeOther_Completes()
    {
        var source = Subject.Create<int>();
        var other = Subject.Create<string>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(other.Values);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);
        await source.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TakeUntil_OtherCompletesWithoutEmitting_SourceContinues()
    {
        var source = Subject.Create<int>();
        var other = Subject.Create<string>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(other.Values);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await other.OnCompletedAsync(Result.Success);
        await source.OnNextAsync(2, CancellationToken.None);

        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TakeUntil_OtherErrors_PropagatesError()
    {
        var source = Subject.Create<int>();
        var other = Subject.Create<string>();
        var expectedException = new InvalidOperationException("other error");

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(other.Values);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await other.OnErrorResumeAsync(expectedException, CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);

        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TakeUntil_EmptySources_Completes()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var other = AsyncObservable.Create<string>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.TakeUntil(other);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task TakeUntil_Dispose_StopsBothSources()
    {
        var sourceDisposed = false;
        var otherDisposed = false;


        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            return AsyncDisposable.Create(() =>
            {
                sourceDisposed = true;
                return default;
            });
        });

        var other = AsyncObservable.Create<string>((observer, token) =>
        {
            return new ValueTask<IAsyncDisposable>(AsyncDisposable.Create(() =>
            {
                otherDisposed = true;
                return default;
            }));
        });

        var takeUntil = source.TakeUntil(other);

        var subscription = await takeUntil.SubscribeAsync(
            delegate{},
            CancellationToken.None);

        await subscription.DisposeAsync();

        sourceDisposed.ShouldBeTrue();
        otherDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeUntil_MultipleOtherEmits_CompletesOnFirst()
    {
        var source = Subject.Create<int>();
        var other = Subject.Create<string>();

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(other.Values);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await other.OnNextAsync("first", CancellationToken.None);
        await other.OnNextAsync("second", CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task TakeUntil_NullSource_ThrowsArgumentNullException()
    {
        AsyncObservable<int> source = null!;
        var other = AsyncObservable.Return("stop");

        Should.Throw<ArgumentNullException>(() => source.TakeUntil(other));
    }

    [Fact]
    public async Task TakeUntil_NullOther_ThrowsArgumentNullException()
    {
        var source = AsyncObservable.Return(1);
        AsyncObservable<string> other = null!;

        Should.Throw<ArgumentNullException>(() => source.TakeUntil(other));
    }

    [Fact]
    public async Task TakeUntil_SourceCompletesWithFailure_PropagatesFailure()
    {
        var source = Subject.Create<int>();
        var other = Subject.Create<string>();
        var expectedException = new InvalidOperationException("failure");

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(other.Values);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnCompletedAsync(Result.Failure(expectedException));

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1 });
    }



    [Fact]
    public async Task TakeUntil_Task_TaskCompletes_CompletesSource()
    {
        var source = Subject.Create<int>();
        var taskTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(taskTcs.Task);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);

        taskTcs.TrySetResult();
            
        await source.OnNextAsync(3, CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
    }
 
    [Fact]
    public async Task TakeUntil_Task_TaskFaults_PropagatesError()
    {
        var source = Subject.Create<int>();
        var taskTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedException = new InvalidOperationException("task error");

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(taskTcs.Task);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        taskTcs.TrySetException(expectedException);

        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task TakeUntil_Task_AlreadyCompletedTask_CompletesImmediately()
    {
        var source = Subject.Create<int>();
        var completedTask = Task.CompletedTask;

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(completedTask);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task TakeUntil_Task_SourceErrors_PropagatesError()
    {
        var source = Subject.Create<int>();
        var taskTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedException = new InvalidOperationException("source error");

        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var takeUntil = source.Values.TakeUntil(taskTcs.Task);

        await using var subscription = await takeUntil.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);

        await source.OnNextAsync(1, CancellationToken.None);
        await source.OnErrorResumeAsync(expectedException, CancellationToken.None);
        await source.OnNextAsync(2, CancellationToken.None);

        var error = await errorTcs.Task;
        error.ShouldBe(expectedException);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TakeUntil_Task_NullSource_ThrowsArgumentNullException()
    {
        AsyncObservable<int> source = null!;
        var task = Task.CompletedTask;

        Should.Throw<ArgumentNullException>(() => source.TakeUntil(task));
    }
}
