using System.Threading.Channels;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests;

public class CoreCoverageTest
{
    static readonly InvalidOperationException ResumeError = new("resume");

    static async Task ShouldFaultOnErrorResume<T>(Func<ManualSource<int>, ValueTask<T>> start)
    {
        var source = new ManualSource<int>();
        var task = start(source).AsTask();
        await source.Observer!.OnErrorResumeAsync(ResumeError, CancellationToken.None);
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await task);
        thrown.ShouldBe(ResumeError);
    }

    [Fact]
    public async Task CountAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.CountAsync(CancellationToken.None));

    [Fact]
    public async Task LongCountAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.LongCountAsync(CancellationToken.None));

    [Fact]
    public async Task ContainsAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.ContainsAsync(1));

    [Fact]
    public async Task AnyAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.AnyAsync());

    [Fact]
    public async Task AllAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.AllAsync(x => true));

    [Fact]
    public async Task FirstOrDefaultAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.FirstOrDefaultAsync(-1));

    [Fact]
    public async Task LastOrDefaultAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.LastOrDefaultAsync(-1));

    [Fact]
    public async Task SingleAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.SingleAsync(CancellationToken.None));

    [Fact]
    public async Task SingleOrDefaultAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.SingleOrDefaultAsync(CancellationToken.None));

    [Fact]
    public async Task ToListAsync_ErrorResume_Faults() =>
        await ShouldFaultOnErrorResume(s => s.ToListAsync(CancellationToken.None));

    [Fact]
    public async Task ForEachAsync_ErrorResume_Faults()
    {
        var source = new ManualSource<int>();
        var task = source.ForEachAsync(x => { }).AsTask();
        await source.Observer!.OnErrorResumeAsync(ResumeError, CancellationToken.None);
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await task);
        thrown.ShouldBe(ResumeError);
    }

    [Fact]
    public async Task WaitCompletionAsync_CompletesWhenSourceCompletes()
    {
        var source = new ManualSource<int>();
        var task = source.WaitCompletionAsync(CancellationToken.None).AsTask();

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        task.IsCompleted.ShouldBeFalse();

        await source.Observer!.OnCompletedAsync(Result.Success);
        await task;
    }

    [Fact]
    public async Task WaitCompletionAsync_ErrorResume_Faults()
    {
        var source = new ManualSource<int>();
        var task = source.WaitCompletionAsync(CancellationToken.None).AsTask();
        await source.Observer!.OnErrorResumeAsync(ResumeError, CancellationToken.None);
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await task);
        thrown.ShouldBe(ResumeError);
    }

    [Fact]
    public async Task ToDictionaryAsync_WithElementSelector_BuildsDictionary()
    {
        var map = await AsyncObservable.Range(1, 3).ToDictionaryAsync(x => x, x => x * 10, cancellationToken: CancellationToken.None);
        map.ShouldBe(new Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 30 });

        await Should.ThrowAsync<ArgumentNullException>(async () => await AsyncObservable.Range(1, 1).ToDictionaryAsync((Func<int, int>)null!, x => x));
        await Should.ThrowAsync<ArgumentNullException>(async () => await AsyncObservable.Range(1, 1).ToDictionaryAsync(x => x, (Func<int, int>)null!));
    }

    [Fact]
    public void Result_TryThrow_And_ToString()
    {
        Result.Success.TryThrow();
        Result.Success.ToString().ShouldBe("Success");

        var exception = new InvalidOperationException("boom");
        var failure = Result.Failure(exception);
        failure.ToString().ShouldBe("Failure{boom}");
        Should.Throw<InvalidOperationException>(() => failure.TryThrow()).ShouldBe(exception);

        Should.Throw<ArgumentNullException>(() => new Result(null!));
    }

    [Fact]
    public async Task AsyncDisposableValue_From_WrapsValueAndDisposable()
    {
        var disposed = false;
        var inner = AsyncDisposable.Create(() => disposed = true);

        var value = AsyncDisposableValue.From(inner);
        value.Value.ShouldBe(inner);
        await value.DisposeAsync();
        disposed.ShouldBeTrue();
    }

    [Fact]
    public void AsyncContext_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => AsyncContext.From((SynchronizationContext)null!));
        Should.Throw<ArgumentNullException>(() => AsyncContext.From((TaskScheduler)null!));
    }

    [Fact]
    public async Task AsyncContext_SwitchContextAsync_CanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await AsyncContext.Default.SwitchContextAsync(forceYielding: true, cts.Token));
    }

    [Fact]
    public async Task AsyncContext_SwitchContextAsync_SwitchesToSynchronizationContext()
    {
        var context = AsyncContext.From(new SynchronizationContext());
        await context.SwitchContextAsync(forceYielding: true, CancellationToken.None);
        AsyncContext.GetCurrent().ShouldNotBeNull();
    }

    [Fact]
    public void Subject_InvalidPublishingOption_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Subject.Create<int>(new SubjectCreationOptions { PublishingOption = (PublishingOption)99 }));
        Should.Throw<ArgumentOutOfRangeException>(() => Subject.CreateBehavior(0, new BehaviorSubjectCreationOptions { PublishingOption = (PublishingOption)99 }));
        Should.Throw<ArgumentOutOfRangeException>(() => Subject.CreateReplayLatest<int>(new ReplayLatestSubjectCreationOptions { PublishingOption = (PublishingOption)99 }));
    }

    [Fact]
    public async Task RefCountTable_Create_GetOrCreateAndDispose()
    {
        var disposed = false;
        var table = RefCountTable.Create<string, int>(async (key, ct) => new AsyncDisposableValue<int>
        {
            Value = key.Length,
            Disposable = AsyncDisposable.Create(() => disposed = true)
        });

        var reference = await table.GetOrCreateAsync("abc", CancellationToken.None);
        reference.Value.ShouldBe(3);
        await reference.DisposeAsync();
        disposed.ShouldBeTrue();
    }

    [Fact]
    public void SubscribeExtensions_NullArguments_Throw()
    {
        AsyncObservable<int> nullSource = null!;
        var source = new ManualSource<int>();

        Should.Throw<ArgumentNullException>(() => nullSource.SubscribeAsync(async (x, ct) => { }, async (e, ct) => { }, async r => { }, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => source.SubscribeAsync((Action<int>)null!, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => source.SubscribeAsync((Action<int>)null!, e => { }, r => { }, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => nullSource.SubscribeAsync(x => { }, e => { }, r => { }, CancellationToken.None));
        Should.Throw<ArgumentNullException>(() => nullSource.SubscribeAsync(async (x, ct) => { }, CancellationToken.None));
    }

    [Fact]
    public async Task ToAsyncEnumerable_NullArguments_Throw()
    {
        AsyncObservable<int> nullSource = null!;
        var source = new ManualSource<int>();

        Should.Throw<ArgumentNullException>(() => nullSource.ToAsyncEnumerable(Channel.CreateUnbounded<int>));
        Should.Throw<ArgumentNullException>(() => source.ToAsyncEnumerable(null!));
        await Should.ThrowAsync<ArgumentNullException>(async () => await nullSource.SubscribeToAsyncEnumerableAsync(Channel.CreateUnbounded<int>));
        await Should.ThrowAsync<ArgumentNullException>(async () => await source.SubscribeToAsyncEnumerableAsync(null!));
    }

    [Fact]
    public void Skip_NegativeCount_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ManualSource<int>().Skip(-1));
    }

    [Fact]
    public void DistinctUntilChanged_NullComparer_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ManualSource<int>().DistinctUntilChanged(null!));
    }

    [Fact]
    public void CreateAsBackgroundJob_NullJob_Throws()
    {
        Should.Throw<ArgumentNullException>(() => AsyncObservable.CreateAsBackgroundJob<int>(null!, TaskScheduler.Default));
    }

    [Fact]
    public async Task OnDispose_Sync_ForwardsErrorResume()
    {
        var source = new ManualSource<int>();
        var disposed = false;
        var errors = new List<Exception>();

        var subscription = await source.OnDispose(() => disposed = true).SubscribeAsync(
            async (x, ct) => { },
            async (e, ct) => errors.Add(e),
            async r => { },
            CancellationToken.None);

        await source.Observer!.OnErrorResumeAsync(ResumeError, CancellationToken.None);
        errors.ShouldBe(new[] { ResumeError });

        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task OnDispose_Async_ForwardsErrorResume()
    {
        var source = new ManualSource<int>();
        var disposed = false;
        var errors = new List<Exception>();

        var subscription = await source.OnDispose(async () => { disposed = true; }).SubscribeAsync(
            async (x, ct) => { },
            async (e, ct) => errors.Add(e),
            async r => { },
            CancellationToken.None);

        await source.Observer!.OnErrorResumeAsync(ResumeError, CancellationToken.None);
        errors.ShouldBe(new[] { ResumeError });

        await subscription.DisposeAsync();
        disposed.ShouldBeTrue();
    }
}
