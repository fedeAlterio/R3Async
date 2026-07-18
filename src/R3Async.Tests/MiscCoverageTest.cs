using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests;

public class MiscCoverageTest
{
    [Fact]
    public async Task Interval_FakeTimeProvider_EmitsTicks()
    {
        var timeProvider = new FakeTimeProvider();
        var period = TimeSpan.FromSeconds(1);

        var results = new List<long>();
        var itemAdded = new SemaphoreSlim(0);

        await using var subscription = await AsyncObservable.Interval(period, timeProvider).SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                itemAdded.Release();
            },
            CancellationToken.None);

        while (!await itemAdded.WaitAsync(TimeSpan.FromMilliseconds(50)))
        {
            timeProvider.Advance(period);
        }

        while (!await itemAdded.WaitAsync(TimeSpan.FromMilliseconds(50)))
        {
            timeProvider.Advance(period);
        }

        results.ShouldBe(new long[] { 1, 2 });
    }

    [Fact]
    public async Task Interval_SystemTime_EmitsFirstTick()
    {
        var value = await AsyncObservable.Interval(TimeSpan.FromMilliseconds(1)).FirstAsync(CancellationToken.None);
        value.ShouldBe(1);
    }

    [Fact]
    public async Task TaskToAsyncObservable_EmitsResultAndCompletes()
    {
        var results = await Task.FromResult(42).ToAsyncObservable().ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task PlainTaskToAsyncObservable_EmitsUnitAndCompletes()
    {
        var results = await Task.CompletedTask.ToAsyncObservable().ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { Unit.Default });
    }

    [Fact]
    public async Task AsyncEnumerableToAsyncObservable_EmitsAllValues()
    {
        static async IAsyncEnumerable<int> Values()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
        }

        var results = await Values().ToAsyncObservable().ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task EnumerableToAsyncObservable_EmitsAllValues()
    {
        var results = await new[] { 1, 2, 3 }.ToAsyncObservable().ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task FromAsync_WithResult_EmitsValue()
    {
        var results = await AsyncObservable.FromAsync(async ct => 42).ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 42 });

        Should.Throw<ArgumentNullException>(() => AsyncObservable.FromAsync((Func<CancellationToken, ValueTask<int>>)null!));
    }

    [Fact]
    public async Task FromAsync_Unit_EmitsUnit()
    {
        var executed = false;
        var results = await AsyncObservable.FromAsync(async ct => { executed = true; }).ToListAsync(CancellationToken.None);

        executed.ShouldBeTrue();
        results.ShouldBe(new[] { Unit.Default });

        Should.Throw<ArgumentNullException>(() => AsyncObservable.FromAsync((Func<CancellationToken, ValueTask>)null!));
    }

    [Fact]
    public async Task Defer_SyncFactory_CreatesPerSubscription()
    {
        var created = 0;
        var observable = AsyncObservable.Defer(() =>
        {
            created++;
            return AsyncObservable.Return(created);
        });

        (await observable.ToListAsync(CancellationToken.None)).ShouldBe(new[] { 1 });
        (await observable.ToListAsync(CancellationToken.None)).ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task Using_DisposesResourceOnCompletion()
    {
        var resourceDisposed = false;

        var observable = AsyncObservable.Using(
            async ct => AsyncDisposable.Create(() => resourceDisposed = true),
            resource => AsyncObservable.Return(42));

        var results = await observable.ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 42 });
        resourceDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Using_ObservableFactoryThrows_DisposesResource()
    {
        var resourceDisposed = false;
        var expected = new InvalidOperationException("boom");

        var observable = AsyncObservable.Using<int, IAsyncDisposable>(
            async ct => AsyncDisposable.Create(() => resourceDisposed = true),
            resource => throw expected);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observable.SubscribeAsync(async (x, ct) => { }, CancellationToken.None));

        thrown.ShouldBe(expected);
        resourceDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsBackgroundJob_LazyStart_EmitsValues()
    {
        var observable = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, ct) =>
        {
            await observer.OnNextAsync(1, ct);
            await observer.OnCompletedAsync(Result.Success);
        });

        var results = await observable.ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task CreateAsBackgroundJob_WithTaskScheduler_EmitsValues()
    {
        var observable = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, ct) =>
        {
            await observer.OnNextAsync(7, ct);
            await observer.OnCompletedAsync(Result.Success);
        }, TaskScheduler.Default);

        var results = await observable.ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 7 });
    }

    [Fact]
    public async Task Yield_ForwardsValuesAndCompletion()
    {
        var results = await AsyncObservable.Range(1, 3).Yield().ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ObserveOn_SynchronizationContext_ForwardsValues()
    {
        var results = await AsyncObservable.Range(1, 3).ObserveOn(new SynchronizationContext()).ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ObserveOn_TaskScheduler_ForwardsValues()
    {
        var results = await AsyncObservable.Range(1, 3).ObserveOn(TaskScheduler.Default).ToListAsync(CancellationToken.None);
        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Publish_Overloads_MulticastValues()
    {
        var source = AsyncObservable.Range(1, 2);

        source.Publish().ShouldNotBeNull();
        source.Publish(SubjectCreationOptions.Default).ShouldNotBeNull();
        source.Publish(0).ShouldNotBeNull();
        source.Publish(0, BehaviorSubjectCreationOptions.Default).ShouldNotBeNull();
        source.ReplayLatestPublish().ShouldNotBeNull();
        source.ReplayLatestPublish(ReplayLatestSubjectCreationOptions.Default).ShouldNotBeNull();
    }

    [Fact]
    public async Task Share_WithStartValue_EmitsStartValueFirst()
    {
        var source = new ManualSource<int>();
        var shared = source.Share(10);

        var results = new List<int>();
        await using var subscription = await shared.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        results.ShouldBe(new[] { 10, 1 });
    }

    [Fact]
    public async Task ShareLatest_ReplaysLatestToNewSubscriber()
    {
        var source = new ManualSource<int>();
        var shared = source.ShareLatest();

        var first = new List<int>();
        await using var s1 = await shared.SubscribeAsync(async (x, token) => first.Add(x), CancellationToken.None);
        await source.Observer!.OnNextAsync(1, CancellationToken.None);

        var second = new List<int>();
        await using var s2 = await shared.SubscribeAsync(async (x, token) => second.Add(x), CancellationToken.None);

        first.ShouldBe(new[] { 1 });
        second.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task CatchAndIgnoreErrorResume_FailureSwitchesToHandler()
    {
        var source = new ManualSource<int>();
        var observable = source.CatchAndIgnoreErrorResume(ex => AsyncObservable.Return(42));

        var results = new List<int>();
        Result? completed = null;

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Failure(new InvalidOperationException("boom")));

        results.ShouldBe(new[] { 1, 42 });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task OnErrorResumeAsFailure_TurnsResumeIntoFailure()
    {
        var source = new ManualSource<int>();
        var observable = source.OnErrorResumeAsFailure();

        Result? completed = null;

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed = result,
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);

        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsFailure.ShouldBeTrue();
        completed!.Value.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task PipeAsync_Subject_ForwardsAllNotifications()
    {
        var source = new ManualSource<int>();
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var errors = new List<Exception>();
        Result? completed = null;

        await using var target = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errors.Add(ex),
            async result => completed = result,
            CancellationToken.None);

        await using var pipe = await source.PipeAsync(subject);

        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 1 });
        errors.ShouldBe(new[] { expected });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task PipeAsync_ChannelWriter_WritesAllValues()
    {
        var channel = Channel.CreateUnbounded<int>();

        await using var pipe = await AsyncObservable.Range(1, 3).PipeAsync(channel.Writer);

        var results = new List<int>();
        await foreach (var value in channel.Reader.ReadAllAsync())
        {
            results.Add(value);
        }

        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task SingleOrDefaultAsync_Empty_ReturnsDefault()
    {
        var result = await AsyncObservable.Empty<int>().SingleOrDefaultAsync(CancellationToken.None);
        result.ShouldBe(0);
    }

    [Fact]
    public async Task SubscribeAsync_Overloads()
    {
        await using var s1 = await AsyncObservable.Return(1).SubscribeAsync();

        var actionResults = new List<int>();
        await using var s2 = await AsyncObservable.Return(2).SubscribeAsync(actionResults.Add, CancellationToken.None);
        actionResults.ShouldBe(new[] { 2 });

        var results = new List<int>();
        var errors = new List<Exception>();
        Result? completed = null;
        var source = new ManualSource<int>();

        await using var s3 = await source.SubscribeAsync(
            results.Add,
            errors.Add,
            r => completed = r,
            CancellationToken.None);

        await source.Observer!.OnNextAsync(3, CancellationToken.None);
        var expected = new InvalidOperationException("resume");
        await source.Observer!.OnErrorResumeAsync(expected, CancellationToken.None);
        await source.Observer!.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 3 });
        errors.ShouldBe(new[] { expected });
        completed.HasValue.ShouldBeTrue();

        var asyncResults = new List<int>();
        await using var s4 = await AsyncObservable.Return(4).SubscribeAsync(async (x, ct) => asyncResults.Add(x));
        asyncResults.ShouldBe(new[] { 4 });
    }

    [Fact]
    public void Unit_EqualityMembers()
    {
        var unit = Unit.Default;

        (unit == default).ShouldBeTrue();
        (unit != default).ShouldBeFalse();
        unit.Equals(default).ShouldBeTrue();
        unit.Equals((object)default(Unit)).ShouldBeTrue();
        unit.Equals("something").ShouldBeFalse();
        unit.GetHashCode().ShouldBe(0);
        unit.ToString().ShouldBe("()");
        Unit.Box.ShouldBeOfType<Unit>();
    }

    [Fact]
    public void Result_Members()
    {
        Result.Success.IsSuccess.ShouldBeTrue();
        Result.Success.IsFailure.ShouldBeFalse();

        var exception = new InvalidOperationException("boom");
        var failure = Result.Failure(exception);
        failure.IsFailure.ShouldBeTrue();
        failure.IsSuccess.ShouldBeFalse();
        failure.Exception.ShouldBe(exception);
    }

    [Fact]
    public void ConcurrentObserverCallsException_HasMessage()
    {
        new ConcurrentObserverCallsException().Message.ShouldContain("Concurrent calls");
    }

    [Fact]
    public async Task Subject_ConcurrentOptions_CreateWorkingSubjects()
    {
        var concurrentOptions = new SubjectCreationOptions { PublishingOption = PublishingOption.Concurrent };
        var subject = Subject.Create<int>(concurrentOptions);

        var results = new List<int>();
        await using var s1 = await subject.Values.SubscribeAsync(async (x, ct) => results.Add(x), CancellationToken.None);
        await subject.OnNextAsync(1, CancellationToken.None);
        results.ShouldBe(new[] { 1 });

        var behavior = Subject.CreateBehavior(5, new BehaviorSubjectCreationOptions { PublishingOption = PublishingOption.Concurrent });
        var behaviorResults = new List<int>();
        await using var s2 = await behavior.Values.SubscribeAsync(async (x, ct) => behaviorResults.Add(x), CancellationToken.None);
        behaviorResults.ShouldBe(new[] { 5 });

        var replay = Subject.CreateReplayLatest<int>(new ReplayLatestSubjectCreationOptions { PublishingOption = PublishingOption.Concurrent });
        await replay.OnNextAsync(7, CancellationToken.None);
        var replayResults = new List<int>();
        await using var s3 = await replay.Values.SubscribeAsync(async (x, ct) => replayResults.Add(x), CancellationToken.None);
        replayResults.ShouldBe(new[] { 7 });
    }

    [Fact]
    public async Task SubjectEx_MapValues_TransformsValuesAndForwardsNotifications()
    {
        var inner = Subject.Create<int>();
        var mapped = inner.MapValues(values => values.Select(x => x * 2));

        var results = new List<int>();
        var errors = new List<Exception>();
        Result? completed = null;

        await using var subscription = await mapped.Values.SubscribeAsync(
            async (x, ct) => results.Add(x),
            async (ex, ct) => errors.Add(ex),
            async r => completed = r,
            CancellationToken.None);

        await mapped.OnNextAsync(1, CancellationToken.None);
        var expected = new InvalidOperationException("resume");
        await mapped.OnErrorResumeAsync(expected, CancellationToken.None);
        await mapped.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 2 });
        errors.ShouldBe(new[] { expected });
        completed.HasValue.ShouldBeTrue();
        completed!.Value.IsSuccess.ShouldBeTrue();
    }
}
