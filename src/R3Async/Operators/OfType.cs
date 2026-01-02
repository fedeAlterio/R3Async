namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<TResult> OfType<TResult>()
            where TResult : class
        {
            return Create<TResult>(async (observer, subscribeToken) =>
            {
                return await @this.SubscribeAsync(async (x, token) =>
                {
                    if (x is TResult v)
                    {
                        await observer.OnNextAsync(v, token);
                    }
                }, observer.OnErrorResumeAsync, observer.OnCompletedAsync, subscribeToken);
            });
        }
    }
}
