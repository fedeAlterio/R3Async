using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> Do(Func<T, CancellationToken, ValueTask>? onNext, 
                                     Func<Exception, CancellationToken, ValueTask>? onErrorResume = null,
                                     Func<Result, CancellationToken, ValueTask>? onCompleted = null)
        {
            return Create<T>(async (observer, subscribeToken) =>
            {
                Func<T, CancellationToken, ValueTask> onNextAsync = onNext != null
                    ? async (value, token) => { await onNext(value, token); await observer.OnNextAsync(value, token); }
                    : observer.OnNextAsync;

                Func<Exception, CancellationToken, ValueTask> onErrorResumeAsync = onErrorResume != null
                    ? async (ex, token) => { await onErrorResume(ex, token); await observer.OnErrorResumeAsync(ex, token); }
                    : observer.OnErrorResumeAsync;

                Func<Result, CancellationToken, ValueTask> onCompletedAsync = onCompleted != null
                    ? async (result, token) => { await onCompleted(result, token); await observer.OnCompletedAsync(result, token); }
                    : observer.OnCompletedAsync;

                return await @this.SubscribeAsync(onNextAsync, onErrorResumeAsync, onCompletedAsync, subscribeToken);
            });
        }

        public AsyncObservable<T> Do(Action<T>? onNext = null,
                                     Action<Exception>? onErrorResume = null,
                                     Action<Result>? onCompleted = null)
        {
            return Create<T>(async (observer, subscribeToken) =>
            {
                Func<T, CancellationToken, ValueTask> onNextAsync = onNext != null
                    ? (value, token) => { onNext(value); return observer.OnNextAsync(value, token); }
                    : observer.OnNextAsync;

                Func<Exception, CancellationToken, ValueTask> onErrorResumeAsync = onErrorResume != null
                    ? (ex, token) => { onErrorResume(ex); return observer.OnErrorResumeAsync(ex, token); }
                    : observer.OnErrorResumeAsync;

                Func<Result, CancellationToken, ValueTask> onCompletedAsync = onCompleted != null
                    ? (result, token) => { onCompleted(result); return observer.OnCompletedAsync(result, token); }
                    : observer.OnCompletedAsync;

                return await @this.SubscribeAsync(onNextAsync, onErrorResumeAsync, onCompletedAsync, subscribeToken);
            });
        }
    }
}
