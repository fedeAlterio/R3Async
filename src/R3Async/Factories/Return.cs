using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Return<T>(T value, bool callOnCompleted = true)
    {
        return CreateAsBackgroundJob<T>(async (obs, token) =>
        {
            await obs.OnNextAsync(value, token);
            if (callOnCompleted)
            {
                await obs.OnCompletedAsync(Result.Success);
            }
        }, true);
    }
}
