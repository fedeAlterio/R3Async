using R3Async.Subjects;
using Shouldly;

namespace R3Async.Tests.Operators;

public class ShareRefCountTest
{
    [Fact(Timeout = 10000)]
    public async Task Share_FailedFirstSubscriptionLeavesNoSourcelessConnection()
    {
        var attempts = new SubscribeAttempts(failingAttempt: 1);
        var source = new ManualSource<int>();
        var shared = source.Share(() => new FailingSubscribeSubject<int>(attempts));

        await Should.ThrowAsync<InvalidOperationException>(async () => await shared.SubscribeAsync());
        var values = new List<int>();
        await using var subscription = await shared.SubscribeAsync((value, _) =>
        {
            values.Add(value);
            return default;
        }, CancellationToken.None);

        source.Observer.ShouldNotBeNull();
        await source.Observer.OnNextAsync(1, CancellationToken.None);
        values.ShouldBe([1]);
    }

    [Fact(Timeout = 10000)]
    public async Task Share_FailedSubscriptionDoesNotKeepTheConnectionAliveAtRefCountZero()
    {
        var attempts = new SubscribeAttempts(failingAttempt: 2);
        var source = new ManualSource<int>();
        var shared = source.Share(() => new FailingSubscribeSubject<int>(attempts), new ShareConfig { ResetOnRefCountZero = true });
        var first = await shared.SubscribeAsync();

        await Should.ThrowAsync<InvalidOperationException>(async () => await shared.SubscribeAsync());
        await first.DisposeAsync();

        source.Disposed.ShouldBeTrue();
    }

    [Fact(Timeout = 10000)]
    public async Task ShareLatest_SubscriberLeavingWhileAnotherJoinsKeepsTheConnection()
    {
        var source = new ManualSource<int>();
        var shared = source.ShareLatest();
        var leaving = await shared.SubscribeAsync();
        await source.Observer!.OnNextAsync(1, CancellationToken.None);
        var values = new List<int>();

        await using var joining = await shared.SubscribeAsync(async (value, _) =>
        {
            values.Add(value);
            await leaving.DisposeAsync();
        }, CancellationToken.None);
        await source.Observer.OnNextAsync(2, CancellationToken.None);

        source.Disposed.ShouldBeFalse();
        values.ShouldBe([1, 2]);
    }

    sealed class SubscribeAttempts(int failingAttempt)
    {
        int _count;

        public bool NextFails() => Interlocked.Increment(ref _count) == failingAttempt;
    }

    sealed class FailingSubscribeSubject<T>(SubscribeAttempts attempts) : ISubject<T>
    {
        readonly ISubject<T> _inner = Subject.Create<T>();

        public AsyncObservable<T> Values => AsyncObservable.Create<T>((observer, token) => attempts.NextFails()
            ? throw new InvalidOperationException("subject subscription fails")
            : _inner.Values.SubscribeAsync(observer.Wrap(), token));

        public ValueTask OnNextAsync(T value, CancellationToken cancellationToken) => _inner.OnNextAsync(value, cancellationToken);

        public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken) => _inner.OnErrorResumeAsync(error, cancellationToken);

        public ValueTask OnCompletedAsync(Result result) => _inner.OnCompletedAsync(result);
    }
}
