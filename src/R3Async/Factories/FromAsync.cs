using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> FromAsync<T>(Func<CancellationToken, ValueTask<T>> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        return CreateAsBackgroundJob<T>(async (obs, token) =>
        {
            var result = await factory(token);
            await obs.OnNextAsync(result, token);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }

    public static AsyncObservable<Unit> FromAsync(Func<CancellationToken, ValueTask> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        return CreateAsBackgroundJob<Unit>(async (obs, token) =>
        {
            await factory(token);
            await obs.OnNextAsync(Unit.Default, token);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }
}
