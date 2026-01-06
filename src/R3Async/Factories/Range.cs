namespace R3Async;

public static partial class AsyncObservable 
{
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
