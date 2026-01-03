using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Return<T>(T value)
    {
        return Create<T>((observer, _) =>
        {
            var subscription = CancelableTaskSubscription.CreateAndStart(async (obs, token) =>
            {
                await obs.OnNextAsync(value, token);
                await obs.OnCompletedAsync(Result.Success);
            }, observer);

            return new(subscription);
        });
    }
}
