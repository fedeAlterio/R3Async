using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Factories;

public class FromSystemObservableTest
{
    [Fact]
    public async Task ToAsyncObservable_Blocking_ForwardsValuesAndCompletion()
    {
        var source = new SystemSubject<int>();
        var observable = source.ToAsyncObservable(BackpressureStrategy.Blocking);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        source.OnNext(1);
        source.OnNext(2);
        source.OnCompleted();

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ToAsyncObservable_Blocking_OnErrorBecomesFailureCompletion()
    {
        var source = new SystemSubject<int>();
        var observable = source.ToAsyncObservable(BackpressureStrategy.Blocking);

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        source.OnError(expected);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task ToAsyncObservable_Blocking_DisposeUnsubscribesFromSource()
    {
        var source = new SystemSubject<int>();
        var observable = source.ToAsyncObservable(BackpressureStrategy.Blocking);

        var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);
        source.ObserverCount.ShouldBe(1);

        await subscription.DisposeAsync();
        source.ObserverCount.ShouldBe(0);
    }

    [Fact]
    public async Task ToAsyncObservable_UnboundedChannel_ForwardsValuesAndCompletion()
    {
        var source = new SystemSubject<int>();
        var observable = source.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel());

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        source.OnNext(1);
        source.OnNext(2);
        source.OnCompleted();

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ToAsyncObservable_UnboundedChannel_OnErrorBecomesFailureCompletion()
    {
        var source = new SystemSubject<int>();
        var observable = source.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel());

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observable.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var expected = new InvalidOperationException("boom");
        source.OnError(expected);

        var result = await completedTcs.Task;
        result.IsFailure.ShouldBeTrue();
        result.Exception.ShouldBe(expected);
    }

    [Fact]
    public async Task ToAsyncObservable_UnboundedChannel_DisposeUnsubscribesFromSource()
    {
        var source = new SystemSubject<int>();
        var observable = source.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel());

        var subscription = await observable.SubscribeAsync(async (x, token) => { }, CancellationToken.None);
        source.ObserverCount.ShouldBe(1);

        await subscription.DisposeAsync();
        source.ObserverCount.ShouldBe(0);
    }

    sealed class SystemSubject<T> : IObservable<T>
    {
        readonly object _gate = new();
        readonly List<IObserver<T>> _observers = new();
        bool _terminated;

        public int ObserverCount
        {
            get
            {
                lock (_gate)
                {
                    return _observers.Count;
                }
            }
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            lock (_gate)
            {
                _observers.Add(observer);
            }

            return new Unsubscriber(this, observer);
        }

        public void OnNext(T value)
        {
            foreach (var observer in Snapshot())
            {
                observer.OnNext(value);
            }
        }

        public void OnError(Exception error)
        {
            if (Terminate()) return;
            foreach (var observer in Snapshot())
            {
                observer.OnError(error);
            }
        }

        public void OnCompleted()
        {
            if (Terminate()) return;
            foreach (var observer in Snapshot())
            {
                observer.OnCompleted();
            }
        }

        bool Terminate()
        {
            lock (_gate)
            {
                if (_terminated) return true;
                _terminated = true;
                return false;
            }
        }

        IObserver<T>[] Snapshot()
        {
            lock (_gate)
            {
                return _observers.ToArray();
            }
        }

        sealed class Unsubscriber(SystemSubject<T> parent, IObserver<T> observer) : IDisposable
        {
            public void Dispose()
            {
                lock (parent._gate)
                {
                    parent._observers.Remove(observer);
                }
            }
        }
    }
}
