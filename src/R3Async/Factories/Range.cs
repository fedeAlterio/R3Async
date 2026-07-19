namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{Int32}"/> that emits <paramref name="count"/> sequential integers starting
    /// at <paramref name="start"/>, then completes successfully. Runs as a cancelable background job, checking for
    /// cancellation before emitting each value.
    /// </summary>
    /// <param name="start">The value of the first integer emitted.</param>
    /// <param name="count">The number of sequential integers to emit.</param>
    public static AsyncObservable<int> Range(int start, int count)
    {
        return CreateAsBackgroundJob<int>(async (observer, cancellationToken) =>
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await observer.OnNextAsync(start + i, cancellationToken);
            }
            await observer.OnCompletedAsync(Result.Success);
        }, true);
    }
}
