using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Subjects;

public class SubjectTest
{
    static ISubject<int> Create(PublishingOption option) => Subject.Create<int>(new SubjectCreationOptions { PublishingOption = option });

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_ForwardsValuesAndCompletion(PublishingOption option)
    {
        var subject = Create(option);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBe(new[] { 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_SubscribeAfterCompleted_ImmediatelyCompletes(PublishingOption option)
    {
        var subject = Create(option);
        await subject.OnCompletedAsync(Result.Success);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.SetResult(result),
            CancellationToken.None);

        var result = await completedTcs.Task;
        result.IsSuccess.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_OnErrorResume_ForwardsErrorButDoesNotComplete(PublishingOption option)
    {
        var subject = Create(option);
        var expected = new InvalidOperationException("fail");

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => { },
            async (ex, token) => errorTcs.SetResult(ex),
            null,
            CancellationToken.None);

        await subject.OnErrorResumeAsync(expected, CancellationToken.None);

        var ex = await errorTcs.Task;
        ex.ShouldBe(expected);
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_Unsubscribe_RemovesObserver(PublishingOption option)
    {
        var subject = Create(option);
        var results = new List<int>();

        var subscription = await subject.Values.SubscribeAsync(async (x, token) => results.Add(x), CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        results.ShouldBe(new[] { 1 });

        await subscription.DisposeAsync();

        await subject.OnNextAsync(2, CancellationToken.None);
        results.ShouldBe(new[] { 1 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_Reentrance_OnNext_CallsAreDelivered(PublishingOption option)
    {
        var subject = Create(option);
        var results = new List<int>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await subject.Values.SubscribeAsync(async (x, token) =>
        {
            results.Add(x);
            if (x == 1)
            {
                await subject.OnNextAsync(2, CancellationToken.None);
            }

            if (x == 2) tcs.SetResult();
        }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await tcs.Task;
        results.ShouldBe(new[] { 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_Reentrance_DisposeDuringOnNext_RemovesObserver(PublishingOption option)
    {
        var subject = Create(option);
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
                tcs.SetResult();
            }
        }, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await tcs.Task;

        await subject.OnNextAsync(3, CancellationToken.None);

        results.ShouldBe(new[] { 1, 2 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task Subject_Reentrance_DisposeDuringOnNext_PreventsOnCompletedCall(PublishingOption option)
    {
        var subject = Create(option);
        var completedCalled = false;
        IAsyncDisposable? subscription = null;

        subscription = await subject.Values.SubscribeAsync(async (x, token) =>
        {
            while (Volatile.Read(ref subscription) is null) await Task.Yield();
            await subscription!.DisposeAsync();
        }, async (ex, token) => { }, async result => completedCalled = true, CancellationToken.None);

        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        completedCalled.ShouldBeFalse();
    }
}
