using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Return<T>(T value)
    {
        return CreateAsBackgroundJob<T>(async (obs, token) =>
        {
            await obs.OnNextAsync(value, token);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }
}
