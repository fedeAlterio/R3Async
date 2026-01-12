using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class MulticastTest
{
    [Fact]
    public async Task Multicast_WithoutConnect_SubscribersDoNotReceiveValues()
    {
        var sourceInvoked = false;
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            sourceInvoked = true;
            await observer.OnNextAsync(1, token);
            return AsyncDisposable.Empty;
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results = new List<int>();
        await using var subscription = await multicast.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        sourceInvoked.ShouldBeFalse();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task Multicast_AfterConnect_SubscribersReceiveValues()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results = new List<int>();
        await using var subscription = await multicast.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);

        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Multicast_MultipleSubscribers_AllReceiveValues()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results1 = new List<int>();
        var results2 = new List<int>();

        await using var sub1 = await multicast.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await using var sub2 = await multicast.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);

        await tcs.Task;
        results1.ShouldBe(new[] { 1, 2 });
        results2.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Multicast_LateSubscriber_ReceivesSubsequentValues()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            tcs1.SetResult();
            await tcs2.Task;
            await observer.OnNextAsync(2, token);
            tcs3.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results1 = new List<int>();
        await using var sub1 = await multicast.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);
        await tcs1.Task;

        var results2 = new List<int>();
        await using var sub2 = await multicast.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        tcs2.SetResult();
        await tcs3.Task;

        results1.ShouldBe(new[] { 1, 2 });
        results2.ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task Multicast_WithBehaviorSubject_LateSubscriberReceivesLastValue()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            tcs1.SetResult();
            await tcs2.Task;
            await observer.OnNextAsync(2, token);
            tcs3.SetResult();
        });

        var subject = Subject.CreateBehavior(0);
        var multicast = source.Multicast(subject);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);
        await tcs1.Task;

        var results = new List<int>();
        await using var subscription = await multicast.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        tcs2.SetResult();
        await tcs3.Task;

        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Multicast_SourceCompletes_AllSubscribersComplete()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var completed1Tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed2Tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sub1 = await multicast.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed1Tcs.SetResult(result),
            CancellationToken.None);

        await using var sub2 = await multicast.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completed2Tcs.SetResult(result),
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);

        await tcs.Task;
        var result1 = await completed1Tcs.Task;
        var result2 = await completed2Tcs.Task;

        result1.IsSuccess.ShouldBeTrue();
        result2.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Multicast_SourceErrors_AllSubscribersReceiveError()
    {
        var expected = new InvalidOperationException("error");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnErrorResumeAsync(expected, token);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var error1Tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error2Tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sub1 = await multicast.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => error1Tcs.SetResult(ex),
            null,
            CancellationToken.None);

        await using var sub2 = await multicast.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => error2Tcs.SetResult(ex),
            null,
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);

        await tcs.Task;
        var error1 = await error1Tcs.Task;
        var error2 = await error2Tcs.Task;

        error1.ShouldBe(expected);
        error2.ShouldBe(expected);
    }

    [Fact]
    public async Task Multicast_DisconnectBeforeConnect_HasNoEffect()
    {
        var source = AsyncObservable.Return(1);
        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var connection = await multicast.ConnectAsync(CancellationToken.None);
        await connection.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Multicast_ConnectTwice_ReturnsSameConnection()
    {
        var subscriptionCount = 0;
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            Interlocked.Increment(ref subscriptionCount);
            return AsyncDisposable.Empty;
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        await using var connection1 = await multicast.ConnectAsync(CancellationToken.None);
        await using var connection2 = await multicast.ConnectAsync(CancellationToken.None);

        subscriptionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Multicast_DisposeConnection_StopsReceivingValues()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            tcs1.SetResult();
            await tcs2.Task.WaitAsync(token);
            await observer.OnNextAsync(2, token);
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results = new List<int>();
        await using var subscription = await multicast.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        var connection = await multicast.ConnectAsync(CancellationToken.None);
        await tcs1.Task;

        await connection.DisposeAsync();
        tcs2.SetResult();

        results.ShouldBe(new[] { 1 });
    }

   

    [Fact]
    public async Task Multicast_UnsubscribeSubscriber_OtherSubscribersStillReceiveValues()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results1 = new List<int>();
        var results2 = new List<int>();

        var sub1 = await multicast.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await using var sub2 = await multicast.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await sub1.DisposeAsync();
        await using var connection = await multicast.ConnectAsync(CancellationToken.None);
        await tcs.Task;

        results1.ShouldBeEmpty();
        results2.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task Multicast_SubscribeAfterCompletion_ReceivesCompletion()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);
        await tcs.Task;

        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await multicast.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
    }


    [Fact]
    public async Task Multicast_ConcurrentSubject_Works()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            tcs.SetResult();
        });

        var subject = Subject.Create<int>(new SubjectCreationOptions { PublishingOption = PublishingOption.Concurrent });
        var multicast = source.Multicast(subject);

        var results1 = new List<int>();
        var results2 = new List<int>();

        await using var sub1 = await multicast.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await using var sub2 = await multicast.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);
        await tcs.Task;

        results1.ShouldBe(new[] { 1 });
        results2.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task Multicast_EmptySource_CompletesImmediately()
    {
        var source = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var subject = Subject.Create<int>();
        var multicast = source.Multicast(subject);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await multicast.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        await using var connection = await multicast.ConnectAsync(CancellationToken.None);
        var result = await completedTcs.Task;

        result.IsSuccess.ShouldBeTrue();
        results.ShouldBeEmpty();
    }
}
