namespace R3Async.Tests;

internal static class TestHelpers
{
    public static async Task WaitForCancellationAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => tcs.TrySetCanceled(token));
        await tcs.Task;
    }
}
