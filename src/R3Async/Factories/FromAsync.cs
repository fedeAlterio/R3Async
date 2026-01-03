using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> FromAsync<T>(Func<CancellationToken, ValueTask<T>> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        return Create<T>((observer, _) =>
        {
            return new(CancelableTaskSubscription.CreateAndStart(async (obs, token) =>
            {
                var result = await factory(token);
                await obs.OnNextAsync(result, token);
                await obs.OnCompletedAsync(Result.Success);
            }, observer));
        });
    }
}
