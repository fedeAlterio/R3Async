namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that emits no values and completes successfully immediately upon subscription.
    /// </summary>
    public static AsyncObservable<T> Empty<T>()
    {
        return Create<T>(async (observer, _) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });
    }
}
