using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

internal sealed class ThrowingSource<T>(Exception? exception = null) : AsyncObservable<T>
{
    public Exception Exception { get; } = exception ?? new InvalidOperationException("subscribe failed");

    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        => throw Exception;
}

public class SubscribeThrowsCoverageTest
{
    static async Task ShouldThrowOnSubscribe<T>(AsyncObservable<T> observable, Exception expected)
    {
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observable.SubscribeAsync(async (x, ct) => { }, CancellationToken.None));
        thrown.ShouldBe(expected);
    }

    [Fact]
    public async Task TakeUntil_Predicate_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        await ShouldThrowOnSubscribe(source.TakeUntil(x => false), source.Exception);
    }

    [Fact]
    public async Task TakeUntil_AsyncPredicate_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        await ShouldThrowOnSubscribe(source.TakeUntil((x, ct) => new ValueTask<bool>(false)), source.Exception);
    }

    [Fact]
    public async Task TakeUntil_CancellationToken_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        using var cts = new CancellationTokenSource();
        await ShouldThrowOnSubscribe(source.TakeUntil(cts.Token), source.Exception);
    }

    [Fact]
    public async Task TakeUntil_Task_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        var tcs = new TaskCompletionSource();
        await ShouldThrowOnSubscribe(source.TakeUntil(tcs.Task), source.Exception);
        tcs.SetResult();
    }

    [Fact]
    public async Task TakeUntil_RawSignal_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        await ShouldThrowOnSubscribe(source.TakeUntil(notify => AsyncDisposable.Empty), source.Exception);
    }

    [Fact]
    public async Task TakeUntil_Other_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        var other = new ManualSource<int>();
        await ShouldThrowOnSubscribe(source.TakeUntil(other), source.Exception);
        other.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TakeUntil_NullArguments_Throw()
    {
        AsyncObservable<int> nullSource = null!;
        var source = new ManualSource<int>();

        Should.Throw<ArgumentNullException>(() => nullSource.TakeUntil(new ManualSource<int>()));
        Should.Throw<ArgumentNullException>(() => source.TakeUntil((AsyncObservable<int>)null!));
        Should.Throw<ArgumentNullException>(() => nullSource.TakeUntil(Task.CompletedTask));
        Should.Throw<ArgumentNullException>(() => source.TakeUntil((Func<int, bool>)null!));
        Should.Throw<ArgumentNullException>(() => source.TakeUntil((Func<int, CancellationToken, ValueTask<bool>>)null!));
        Should.Throw<ArgumentNullException>(() => source.TakeUntil((CompletionObservableDelegate)null!));
    }

    [Fact]
    public async Task Merge_OuterSubscribeThrows_Rethrows()
    {
        var outer = new ThrowingSource<AsyncObservable<int>>();
        await ShouldThrowOnSubscribe(outer.Merge(), outer.Exception);
    }

    [Fact]
    public async Task Merge_MaxConcurrency_OuterSubscribeThrows_Rethrows()
    {
        var outer = new ThrowingSource<AsyncObservable<int>>();
        await ShouldThrowOnSubscribe(outer.Merge(2), outer.Exception);
    }

    [Fact]
    public async Task Merge_InnerSubscribeThrows_CompletesWithFailure()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner = new ThrowingSource<int>();

        Result? completed = null;
        await using var subscription = await outer.Merge().SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completed = result,
            CancellationToken.None);

        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(inner.Exception);
    }

    [Fact]
    public async Task Merge_MaxConcurrency_InnerSubscribeThrows_CompletesWithFailure()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner = new ThrowingSource<int>();

        var errors = new List<Exception>();
        Result? completed = null;
        await using var subscription = await outer.Merge(2).SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => errors.Add(ex),
            async result => completed = result,
            CancellationToken.None);

        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        errors.ShouldBeEmpty();
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(inner.Exception);
    }

    [Fact]
    public async Task Merge_MaxConcurrency_SlotFreedByCompletedInner_IsReusable()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner1 = new ManualSource<int>();
        var inner2 = new ManualSource<int>();

        var results = new List<int>();
        await using var subscription = await outer.Merge(1).SubscribeAsync(
            async (x, ct) => results.Add(x),
            CancellationToken.None);

        await outer.Observer!.OnNextAsync(inner1, CancellationToken.None);
        await inner1.Observer!.OnNextAsync(1, CancellationToken.None);
        await inner1.Observer!.OnCompletedAsync(Result.Success);

        await outer.Observer!.OnNextAsync(inner2, CancellationToken.None);
        await inner2.Observer!.OnNextAsync(2, CancellationToken.None);

        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task MergeEnumerable_InnerSubscribeThrows_CompletesWithFailure()
    {
        var inner = new ThrowingSource<int>();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await new AsyncObservable<int>[] { inner }.Merge().SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.IsFailure.ShouldBeTrue();
        completed.Exception.ShouldBe(inner.Exception);
    }

    [Fact]
    public async Task MergeEnumerable_EnumerationThrows_CompletesWithFailure()
    {
        var expected = new InvalidOperationException("enumeration failed");

        IEnumerable<AsyncObservable<int>> Sources()
        {
            yield return AsyncObservable.Return(1);
            throw expected;
        }

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await Sources().Merge().SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.IsFailure.ShouldBeTrue();
        completed.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task Switch_OuterSubscribeThrows_Rethrows()
    {
        var outer = new ThrowingSource<AsyncObservable<int>>();
        await ShouldThrowOnSubscribe(outer.Switch(), outer.Exception);
    }

    [Fact]
    public async Task Switch_InnerSubscribeThrows_CompletesWithFailure()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner = new ThrowingSource<int>();

        Result? completed = null;
        await using var subscription = await outer.Switch().SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completed = result,
            CancellationToken.None);

        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(inner.Exception);
    }

    [Fact]
    public async Task Concat_OuterSubscribeThrows_Rethrows()
    {
        var outer = new ThrowingSource<AsyncObservable<int>>();
        await ShouldThrowOnSubscribe(outer.Concat(), outer.Exception);
    }

    [Fact]
    public async Task Concat_InnerSubscribeThrows_CompletesWithFailure()
    {
        var outer = new ManualSource<AsyncObservable<int>>();
        var inner = new ThrowingSource<int>();

        Result? completed = null;
        await using var subscription = await outer.Concat().SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completed = result,
            CancellationToken.None);

        await outer.Observer!.OnNextAsync(inner, CancellationToken.None);

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(inner.Exception);
    }

    [Fact]
    public async Task ConcatEnumerable_InnerSubscribeThrows_CompletesWithFailure()
    {
        var inner = new ThrowingSource<int>();

        Result? completed = null;
        await using var subscription = await new AsyncObservable<int>[] { inner }.Concat().SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completed = result,
            CancellationToken.None);

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(inner.Exception);
    }

    [Fact]
    public async Task Throttle_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        await ShouldThrowOnSubscribe(source.ThrottleFirst(TimeSpan.FromSeconds(1)), source.Exception);
    }

    [Fact]
    public async Task Debounce_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        await ShouldThrowOnSubscribe(source.Debounce(TimeSpan.FromSeconds(1)), source.Exception);
    }

    [Fact]
    public async Task GroupBy_SourceSubscribeThrows_Rethrows()
    {
        var source = new ThrowingSource<int>();
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await source.GroupBy(x => x).SubscribeAsync(async (g, ct) => { }, CancellationToken.None));
        thrown.ShouldBe(source.Exception);
    }

    [Fact]
    public async Task GroupBy_NullArguments_Throw()
    {
        AsyncObservable<int> nullSource = null!;
        var source = new ManualSource<int>();

        Should.Throw<ArgumentNullException>(() => nullSource.GroupBy(x => x));
        Should.Throw<ArgumentNullException>(() => source.GroupBy<int, int>(null!));
        Should.Throw<ArgumentNullException>(() => nullSource.GroupBy(x => x, key => R3Async.Subjects.Subject.Create<int>()));
        Should.Throw<ArgumentNullException>(() => source.GroupBy<int, int>(null!, key => R3Async.Subjects.Subject.Create<int>()));
    }

    [Fact]
    public async Task Prepend_SourceSubscribeCanceled_StopsSilently()
    {
        var canceled = new ThrowingSource<int>(new OperationCanceledException());

        var results = new List<int>();
        await using var subscription = await canceled.Prepend(1).SubscribeAsync(
            async (x, ct) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task Prepend_SourceSubscribeThrows_CompletesWithFailure()
    {
        var source = new ThrowingSource<int>();

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await source.Prepend([1, 2]).SubscribeAsync(
            async (x, ct) => { },
            async (ex, ct) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var completed = await completedTcs.Task;
        completed.IsFailure.ShouldBeTrue();
        completed.Exception.ShouldBe(source.Exception);
    }
}
