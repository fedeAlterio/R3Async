namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Empty<T>()
    {
        return Create<T>(async (observer, _) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });
    }
}
