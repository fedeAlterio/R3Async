using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that emits a single <paramref name="value"/> upon subscription,
    /// running as a cancelable background job.
    /// </summary>
    /// <param name="value">The single value to emit.</param>
    /// <param name="callOnCompleted">
    /// When <c>true</c> (the default), the observable completes successfully after emitting <paramref name="value"/>.
    /// When <c>false</c>, the observable stays open indefinitely after emitting the value, without completing.
    /// </param>
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
