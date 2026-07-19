using R3Async.Subjects;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests;

public class SubscribeExtensionsTest
{
    [Fact]
    public async Task SubscribeAsync_AsyncOnNextWithSyncCallbacks_ForwardsEverything()
    {
        var subject = Subject.Create<int>();

        var results = new List<int>();
        var errors = new List<Exception>();
        Result? completed = null;

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            onErrorResume: ex => errors.Add(ex),
            onCompleted: result => completed = result,
            CancellationToken.None);

        var expectedError = new InvalidOperationException("boom");
        await subject.OnNextAsync(1, CancellationToken.None);
        await subject.OnErrorResumeAsync(expectedError, CancellationToken.None);
        await subject.OnNextAsync(2, CancellationToken.None);
        await subject.OnCompletedAsync(Result.Success);

        results.ShouldBe(new[] { 1, 2 });
        errors.ShouldBe(new[] { expectedError });
        completed.ShouldNotBeNull();
        completed.Value.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_AsyncOnNextWithSyncCallbacks_NullCallbacksAreAllowed()
    {
        var subject = Subject.Create<int>();
        var results = new List<int>();

        await using var subscription = await subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            onErrorResume: null,
            onCompleted: null,
            CancellationToken.None);

        await subject.OnNextAsync(42, CancellationToken.None);

        results.ShouldBe(new[] { 42 });
    }
}
