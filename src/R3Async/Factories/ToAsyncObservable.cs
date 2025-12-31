using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> ToAsyncObservable<T>(this Task<T> @this)
    {
        return Create<T>((observer, _) =>
        {
            var cts = new CancellationTokenSource();
            AsyncLocal<bool> reentrant = new();
            Task task = Core(cts.Token);

            async Task Core(CancellationToken cancellationToken)
            {
                try
                {
                    reentrant.Value = true;
                    var result = await @this.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
                    await observer.OnNextAsync(result, cancellationToken);
                    await observer.OnCompletedAsync(Result.Success);
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
                cts.Cancel();
                if (!reentrant.Value)
                {
                    await task;
                }
                cts.Dispose();
            });

            return new ValueTask<IAsyncDisposable>(subcription);
        });
    }

    public static AsyncObservable<T> ToAsyncObservable<T>(this IAsyncEnumerable<T> @this)
    {
        return Create<T>((observer, _) =>
        {
            var cts = new CancellationTokenSource();
            AsyncLocal<bool> reentrant = new();
            Task task = Core(cts.Token);

            async Task Core(CancellationToken cancellationToken)
            {
                try
                {
                    reentrant.Value = true;
                    await foreach (var value in @this.WithCancellation(cancellationToken))
                    {
                        await observer.OnNextAsync(value, cancellationToken);
                    }

                    await observer.OnCompletedAsync(Result.Success);
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
                cts.Cancel();
                if (!reentrant.Value)
                {
                    await task;
                }
                cts.Dispose();
            });

            return new ValueTask<IAsyncDisposable>(subcription);
        });
    }

    public static AsyncObservable<T> ToAsyncObservable<T>(this IEnumerable<T> @this)
    {
        return Create<T>((observer, _) =>
        {
            var cts = new CancellationTokenSource();
            AsyncLocal<bool> reentrant = new();
            Task task = Core(cts.Token);
            async Task Core(CancellationToken cancellationToken)
            {
                try
                {
                    reentrant.Value = true;
                    foreach (var value in @this)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        await observer.OnNextAsync(value, cancellationToken);
                    }
                    await observer.OnCompletedAsync(Result.Success);
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
                cts.Cancel();
                if (!reentrant.Value)
                {
                    await task;
                }
                cts.Dispose();
            });
            return new ValueTask<IAsyncDisposable>(subcription);
        });
    }
}
