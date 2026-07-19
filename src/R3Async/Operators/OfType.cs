namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Filters the source sequence to only the values that are of type <typeparamref name="TResult"/>, casting them along the way.
        /// Values that are not of the target type are silently dropped.
        /// </summary>
        /// <typeparam name="TResult">The reference type to filter and cast values to.</typeparam>
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
