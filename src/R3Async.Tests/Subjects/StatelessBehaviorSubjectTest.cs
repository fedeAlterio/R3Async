using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Subjects;

public class StatelessBehaviorSubjectTest
{
    static ISubject<int> CreateBehavior(int startValue, PublishingOption option) =>
        Subject.CreateBehavior(startValue, new BehaviorSubjectCreationOptions { PublishingOption = option, IsStateless = true });

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task StatelessBehaviorSubject_InitialSubscriber_ReceivesStartValue(PublishingOption option)
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
    public async Task StatelessBehaviorSubject_ValueResets_WhenAllSubscribersUnsubscribe(PublishingOption option)
    {
        var subject = CreateBehavior(1, option);

        var results1 = new List<int>();
        var results2 = new List<int>();

        var sub1 = await subject.Values.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await subject.OnNextAsync(2, CancellationToken.None);

        var sub2 = await subject.Values.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await subject.OnNextAsync(3, CancellationToken.None);

        await sub1.DisposeAsync();
        await sub2.DisposeAsync();

        var results3 = new List<int>();
        await using var sub3 = await subject.Values.SubscribeAsync(
            async (x, token) => results3.Add(x),
            CancellationToken.None);

        results1.ShouldBe(new[] { 1, 2, 3 });
        results2.ShouldBe(new[] { 2, 3 });
        results3.ShouldBe(new[] { 1 });
    }

    [Theory]
    [InlineData(PublishingOption.Serial)]
    [InlineData(PublishingOption.Concurrent)]
    public async Task StatelessBehaviorSubject_OnCompleted_ClearsSubscribersAndResetsValue(PublishingOption option)
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

        var results2 = new List<int>();
        await using var subscription2 = await subject.Values.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        results2.ShouldBe(new[] { 1 });
    }
}
