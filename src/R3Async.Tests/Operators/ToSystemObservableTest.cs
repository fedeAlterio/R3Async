using R3Async.Subjects;
using Shouldly;

namespace R3Async.Tests.Operators;

public class ToSystemObservableTest
{
    static ToObservableConfiguration BlockingConfiguration { get; } = new()
    {
        SubscribeStrategy = AsyncToSyncStrategy.Blocking,
        DisposeStrategy = AsyncToSyncStrategy.Blocking,
    };

    [Fact]
    public async Task ToSystemObservable_ForwardsValuesAndCompletion()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToSystemObservable(BlockingConfiguration);

        var observer = new RecordingObserver<int>();
        using var subscription = observable.Subscribe(observer);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        observer.Values.ShouldBe(new[] { 1, 2 });
        observer.Completed.ShouldBeTrue();
        observer.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ToSystemObservable_FailureCompletionBecomesOnError()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToSystemObservable(BlockingConfiguration);

        var observer = new RecordingObserver<int>();
        using var subscription = observable.Subscribe(observer);

        var expected = new InvalidOperationException("boom");
        await subject.OnCompletedAsync(Result.Failure(expected));

        observer.Completed.ShouldBeFalse();
        observer.Error.ShouldBe(expected);
    }

    [Fact]
    public async Task ToSystemObservable_OnErrorResumeTerminatesWithOnError()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToSystemObservable(BlockingConfiguration);

        var observer = new RecordingObserver<int>();
        using var subscription = observable.Subscribe(observer);

        var expected = new InvalidOperationException("boom");
        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnErrorResumeAsync(expected, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);

        observer.Values.ShouldBe(new[] { 1 });
        observer.Error.ShouldBe(expected);
        observer.Completed.ShouldBeFalse();
    }

    [Fact]
    public async Task ToSystemObservable_DisposeStopsDelivery()
    {
        var subject = Subject.Create<int>();
        var observable = subject.Values.ToSystemObservable(BlockingConfiguration);

        var observer = new RecordingObserver<int>();
        var subscription = observable.Subscribe(observer);

        await subject.OnNextAsync(1, CancellationToken.None);
        subscription.Dispose();
        await subject.OnNextAsync(2, CancellationToken.None);

        observer.Values.ShouldBe(new[] { 1 });
        observer.Completed.ShouldBeFalse();
        observer.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ToSystemObservable_FireAndForgetStrategiesDeliverValues()
    {
        var configuration = new ToObservableConfiguration
        {
            SubscribeStrategy = AsyncToSyncStrategy.FireAndForget(),
            DisposeStrategy = AsyncToSyncStrategy.FireAndForget(),
        };

        var subject = Subject.Create<int>();
        var observable = subject.Values.ToSystemObservable(configuration);

        var observer = new RecordingObserver<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.OnCompletedCallback = () => completedTcs.TrySetResult(true);

        using var subscription = observable.Subscribe(observer);

        await subject.OnNextAsync(42, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        (await completedTcs.Task).ShouldBeTrue();
        observer.Values.ShouldBe(new[] { 42 });
    }

    sealed class RecordingObserver<T> : IObserver<T>
    {
        public List<T> Values { get; } = new();
        public Exception? Error { get; private set; }
        public bool Completed { get; private set; }
        public Action? OnCompletedCallback { get; set; }

        public void OnNext(T value) => Values.Add(value);
        public void OnError(Exception error) => Error = error;

        public void OnCompleted()
        {
            Completed = true;
            OnCompletedCallback?.Invoke();
        }
    }
}
