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
        public AsyncObservable<T> Prepend(T value) => @this.Prepend([value]);
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
                var subcription = AsyncDisposable.Create(async () =>
                {
                    await subscriptionDisposable.DisposeAsync();
                    if (!reentrant.Value)
                    {
                        cts.Cancel();
                        await task;
                    }
                    cts.Dispose();
                });
                return new ValueTask<IAsyncDisposable>(subcription);
            });
        }
    }
}
