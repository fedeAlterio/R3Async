using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> source)
    {
        public AsyncObservable<T> Catch(Func<Exception, AsyncObservable<T>> handler,
                                        Func<Exception, CancellationToken, ValueTask>? onErrorResume = null)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            return Create<T>(async (observer, cancellationToken) =>
            {
                var onErrorResumeAsync = onErrorResume ?? observer.OnErrorResumeAsync;
                SingleAssignmentAsyncDisposable handlerDisposable = new();
                IAsyncDisposable sourceDisposable = await source.SubscribeAsync(
                    async (value, ct) => await observer.OnNextAsync(value, ct),
                    onErrorResumeAsync,
                    async result =>
                    {
                        if (result.IsSuccess)
                        {
                            await observer.OnCompletedAsync(result);
                            return;
                        }

                        try
                        {
                            var handlerObservable = handler(result.Exception);
                            var handlerSubscription = await handlerObservable.SubscribeAsync(observer.Wrap(), cancellationToken);
                            await handlerDisposable.SetDisposableAsync(handlerSubscription);
                        }
                        catch (Exception e)
                        {
                            await observer.OnCompletedAsync(Result.Failure(e));
                        }
                    },
                    cancellationToken);
                return AsyncDisposable.Create(async () =>
                {
                    await sourceDisposable.DisposeAsync();
                    await handlerDisposable.DisposeAsync();
                });
            });
        }

        public AsyncObservable<T> CatchAndIgnoreErrorResume(Func<Exception, AsyncObservable<T>> handler) => source.Catch(handler, static (error, _) =>
        {
            UnhandledExceptionHandler.OnUnhandledException(error);
            return default;
        });
    }
}
