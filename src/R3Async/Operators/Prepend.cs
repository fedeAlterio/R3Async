using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Emits <paramref name="value"/> immediately upon subscription, before subscribing to the source and forwarding its values.
        /// </summary>
        public AsyncObservable<T> Prepend(T value) => @this.Prepend([value]);

        /// <summary>
        /// Emits <paramref name="values"/> in order immediately upon subscription, before subscribing to the source and forwarding its values.
        /// </summary>
        /// <remarks>If disposed while the prepended values are still being emitted, emission stops as soon as possible and the source is never subscribed to.</remarks>
        public AsyncObservable<T> Prepend(IEnumerable<T> values)
        {
            return Create<T>((observer, _) =>
            {
                var cts = new CancellationTokenSource();
                SingleAssignmentAsyncDisposable subscriptionDisposable = new();
                AsyncLocal<bool> reentrant = new();
                Task task = Core(cts.Token);
                async Task Core(CancellationToken cancellationToken)
                {
                    try
                    {
                        reentrant.Value = true;
                        foreach (var value in values)
                        {
                            if (cancellationToken.IsCancellationRequested) return;
                            await observer.OnNextAsync(value, cancellationToken);
                        }

                        var subscription = await @this.SubscribeAsync(observer.Wrap(), cancellationToken);
                        await subscriptionDisposable.SetDisposableAsync(subscription);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception e)
                    {
                        try
                        {
                            await observer.OnCompletedAsync(Result.Failure(e));
                        }
                        catch (Exception exception)
                        {
                            UnhandledExceptionHandler.OnUnhandledException(exception);
                        }
                    }
                }
                var subscription = AsyncDisposable.Create(async () =>
                {
                    await subscriptionDisposable.DisposeAsync();
                    if (!reentrant.Value)
                    {
                        cts.Cancel();
                        await task;
                    }
                    cts.Dispose();
                });
                return new ValueTask<IAsyncDisposable>(subscription);
            });
        }
    }
}
