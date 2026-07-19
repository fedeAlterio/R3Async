using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Subjects;
using Shouldly;
using Xunit;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ShareTest
{
    [Fact]
    public async Task Share_SharesSubscription()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig());

        await using var sub1 = await shared.SubscribeAsync();
        await using var sub2 = await shared.SubscribeAsync();

        subscriptionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Share_SourceSubscribeFailure_DoesNotPoisonLaterSubscribers()
    {
        var subscribeAttempts = 0;
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            if (Interlocked.Increment(ref subscribeAttempts) == 1)
                throw new InvalidOperationException("subscribe fails");

            await observer.OnNextAsync(42, token);
            return AsyncDisposable.Empty;
        });

        var shared = source.Share();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await shared.SubscribeAsync(async (x, token) => { }, CancellationToken.None));

        var results = new List<int>();
        await using var sub = await shared.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);
        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task Share_ResetOnRefCountZero_ResubscribesToSource()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig { ResetOnRefCountZero = true });

        await using (await shared.SubscribeAsync())
        {
            subscriptionCount.ShouldBe(1);
        }

        // RefCount reached zero, so next subscription should trigger resubscribe
        await using (await shared.SubscribeAsync())
        {
            subscriptionCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Share_NoResetOnRefCountZero_DoesNotResubscribeToSource()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig { ResetOnRefCountZero = false });

        await using (await shared.SubscribeAsync())
        {
            subscriptionCount.ShouldBe(1);
        }

        // RefCount reached zero, but ResetOnRefCountZero is false
        await using (await shared.SubscribeAsync())
        {
            subscriptionCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Share_ResetOnSuccessResult_ResubscribesToSource()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig { ResetOnSuccessResult = true });

        await using var sub1 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(1);

        await using var sub2 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(2);
    }
    
    [Fact]
    public async Task Share_NoResetOnSuccessResult_DoesNotResubscribeToSource()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig { ResetOnSuccessResult = false });

        await using var sub1 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(1);

        await using var sub2 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Share_ResetOnErrorResult_ResubscribesToSource()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            await observer.OnCompletedAsync(Result.Failure(new Exception("test")));
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig { ResetOnErrorResult = true });

        await using var sub1 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(1);

        await using var sub2 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(2);
    }
    
    [Fact]
    public async Task Share_NoResetOnErrorResult_DoesNotResubscribeToSource()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, ct) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            await observer.OnCompletedAsync(Result.Failure(new Exception("test")));
            return AsyncDisposable.Empty;
        });

        var shared = source.Share(() => Subject.Create<int>(), new ShareConfig { ResetOnErrorResult = false });

        await using var sub1 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(1);

        await using var sub2 = await shared.SubscribeAsync();
        subscriptionCount.ShouldBe(1);
    }
}
