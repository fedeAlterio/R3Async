using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Discards every value from the source sequence, forwarding only resumable errors and the final completion.
        /// Useful to await a sequence's termination when its values are irrelevant.
        /// </summary>
        public AsyncObservable<T> IgnoreValues()
        {
            return Create<T>(async (observer, subscribeToken) =>
                await @this.SubscribeAsync(
                    static (_, _) => default,
                    observer.OnErrorResumeAsync,
                    observer.OnCompletedAsync,
                    subscribeToken));
        }
    }
}
