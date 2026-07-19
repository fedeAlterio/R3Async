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
        /// <summary>
        /// Recovers from a failure completion of <paramref name="source"/> by subscribing to a fallback observable
        /// produced by <paramref name="handler"/>. Values and <c>OnErrorResumeAsync</c> notifications from
        /// <paramref name="source"/> are passed through unchanged; only a failure <em>completion</em> triggers the
        /// handler. If <paramref name="handler"/> itself throws, the stream completes with that failure instead.
        /// </summary>
        /// <param name="handler">Invoked with the failure exception to produce the observable to switch to.</param>
        /// <param name="onErrorResume">
        /// Optional override for how <c>OnErrorResumeAsync</c> notifications from <paramref name="source"/> are
        /// handled before the fallback kicks in. Defaults to forwarding them to the observer's <c>OnErrorResumeAsync</c>.
        /// </param>
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

        /// <summary>
        /// Same as <see cref="Catch(AsyncObservable{T}, Func{Exception, AsyncObservable{T}}, Func{Exception, CancellationToken, ValueTask}?)"/>,
        /// except <c>OnErrorResumeAsync</c> notifications from <paramref name="source"/> are routed to the
        /// <see cref="UnhandledExceptionHandler"/> instead of being forwarded to the observer.
        /// </summary>
        /// <param name="handler">Invoked with the failure exception to produce the observable to switch to.</param>
        public AsyncObservable<T> CatchAndIgnoreErrorResume(Func<Exception, AsyncObservable<T>> handler) => source.Catch(handler, static (error, _) =>
        {
            UnhandledExceptionHandler.OnUnhandledException(error);
            return default;
        });
    }
}
