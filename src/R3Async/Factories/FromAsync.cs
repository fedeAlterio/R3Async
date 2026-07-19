using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that, on subscription, runs <paramref name="factory"/> as a cancelable
    /// background job, emits its result as the single value, and then completes successfully. Disposing the subscription
    /// cancels the token passed to <paramref name="factory"/> and waits for it to observe the cancellation.
    /// </summary>
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

    /// <summary>
    /// Creates an <see cref="AsyncObservable{Unit}"/> that, on subscription, runs <paramref name="factory"/> as a cancelable
    /// background job, then emits <see cref="Unit.Default"/> and completes successfully. Disposing the subscription
    /// cancels the token passed to <paramref name="factory"/> and waits for it to observe the cancellation.
    /// </summary>
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
