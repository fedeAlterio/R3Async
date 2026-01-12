using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Subjects;

public class BehaviorSubjectTest
{
    static ISubject<int> CreateBehavior(int startValue, PublishingOption option) => 
        Subject.CreateBehavior(startValue, new BehaviorSubjectCreationOptions { PublishingOption = option });

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_NewSubscriber_ReceivesLastValue(PublishingOption option)
    {
        var subject = CreateBehavior(42, option);
        var results = new List<int>();

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 42 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_UpdateValue_SubscriberReceivesNewValue(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var results = new List<int>();

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);

        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_LateSubscriber_ReceivesCurrentValue(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);

        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);

        var results = new List<int>();
        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 3 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_MultipleSubscribers_AllReceiveValues(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var results1 = new List<int>();
        var results2 = new List<int>();

        await using var sub1 = await subject.Values.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await subject.OnNextAsync(2, CancellationToken.None);

        await using var sub2 = await subject.Values.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await subject.OnNextAsync(3, CancellationToken.None);

        results1.ShouldBe(new[] { 1, 2, 3 });
        results2.ShouldBe(new[] { 2, 3 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_OnCompleted_ForwardsCompletion(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_SubscribeAfterCompleted_ImmediatelyCompletes(PublishingOption option)
    {
        var subject = CreateBehavior(42, option);
        await subject.OnCompletedAsync(Result.Success);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_OnCompletedWithFailure_ForwardsFailure(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var expected = new InvalidOperationException("fail");
        var completedTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result => { if (result.IsFailure) completedTcs.TrySetResult(result.Exception); },
            CancellationToken.None);

        await subject.OnCompletedAsync(Result.Failure(expected));

        var ex = await completedTcs.Task;
        ex.ShouldBe(expected);
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_OnErrorResume_ForwardsError(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var expected = new InvalidOperationException("error");
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);

        await subject.OnErrorResumeAsync(expected, CancellationToken.None);

        var ex = await errorTcs.Task;
        ex.ShouldBe(expected);
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_OnErrorResume_ContinuesAfterError(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            null,
            CancellationToken.None);

        await subject.OnErrorResumeAsync(new InvalidOperationException(), CancellationToken.None);
        await errorTcs.Task;

        await subject.OnNextAsync(2, CancellationToken.None);

        results.ShouldBe(new[] { 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_Unsubscribe_RemovesObserver(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var results = new List<int>();

        var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        await subject.OnNextAsync(2, CancellationToken.None);
        results.ShouldBe(new[] { 1, 2 });

        await subscription.DisposeAsync();

        await subject.OnNextAsync(3, CancellationToken.None);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_AfterCompletion_IgnoresNewValues(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnCompletedAsync(Result.Success);
        await completedTcs.Task;

        await subject.OnNextAsync(2, CancellationToken.None);

        results.ShouldBe(new[] { 1 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_AfterCompletion_IgnoresOnErrorResume(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var errorCalled = false;
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errorCalled = true,
            async result => completedTcs.TrySetResult(result),
            CancellationToken.None);

        await subject.OnCompletedAsync(Result.Success);
        await completedTcs.Task;

        await subject.OnErrorResumeAsync(new InvalidOperationException(), CancellationToken.None);

        errorCalled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_AfterCompletion_IgnoresDuplicateCompletion(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);
        var completionCount = 0;
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => { },
            async result =>
            {
                completionCount++;
                completedTcs.TrySetResult(result);
            },
            CancellationToken.None);

        await subject.OnCompletedAsync(Result.Success);
        await completedTcs.Task;

        await subject.OnCompletedAsync(Result.Success);

        completionCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_Reentrance_OnNext_CallsAreDelivered(PublishingOption option)
    {
        var subject = CreateBehavior(0, option);
        var results = new List<int>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(async (x, token) =>
        {
            results.Add(x);
            if (x == 1)
            {
                await subject.OnNextAsync(2, CancellationToken.None);
            }

            if (x == 2) tcs.TrySetResult();
        }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await tcs.Task;
        results.ShouldBe(new[] { 0, 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_Reentrance_DisposeDuringOnNext_RemovesObserver(PublishingOption option)
    {
        var subject = CreateBehavior(0, option);
        var results = new List<int>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncDisposable? subscription = null;

        subscription = await subject.Values.SubscribeAsync(async (x, token) =>
        {
            results.Add(x);
            if (x == 1)
            {
                while (Volatile.Read(ref subscription) is null) await Task.Yield();
                await subject.OnNextAsync(2, token);
                await subscription!.DisposeAsync();
                tcs.TrySetResult();
            }
        }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await tcs.Task;

        await subject.OnNextAsync(3, CancellationToken.None);

        results.ShouldBe(new[] { 0, 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_Reentrance_DisposeDuringInitialValue_PreventsSubsequentCalls(PublishingOption option)
    {
        var subject = CreateBehavior(42, option);
        var results = new List<int>();
        IAsyncDisposable? subscription = null;

        subscription = await subject.Values.Skip(1).SubscribeAsync(async (x, token) =>
        {
            results.Add(x);
            while (Volatile.Read(ref subscription) is null) await Task.Yield();
            await subscription!.DisposeAsync();
        }, CancellationToken.None);

        await subject.OnNextAsync(100, CancellationToken.None);
        await subject.OnNextAsync(102, CancellationToken.None);

        results.ShouldBe([100]);
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_ReferenceTypeStartValue_RetainsSameReference(PublishingOption option)
    {
        var startObject = new object();
        var subject = Subject.CreateBehavior(startObject, new BehaviorSubjectCreationOptions { PublishingOption = option });
        object? receivedObject = null;

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => receivedObject = x,
            CancellationToken.None);

        ReferenceEquals(receivedObject, startObject).ShouldBeTrue();
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_NullStartValue_DeliversNull(PublishingOption option)
    {
        var subject = Subject.CreateBehavior<string?>(null, new BehaviorSubjectCreationOptions { PublishingOption = option });
        var received = false;
        string? receivedValue = "not null";

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) =>
            {
                received = true;
                receivedValue = x;
            },
            CancellationToken.None);

        received.ShouldBeTrue();
        receivedValue.ShouldBeNull();
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_MultipleSequentialUpdates_LastValuePersists(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);

        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);
        await subject.OnNextAsync(4, CancellationToken.None);

        var results = new List<int>();
        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 4 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task BehaviorSubject_NoSubscribers_UpdatesValue(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);

        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnNextAsync(3, CancellationToken.None);

        var results = new List<int>();
        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 3 });
    }
}
