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
            var subscription = AsyncOperationSubscription.CreateAndRun(async (obs, cancellationToken) =>
            {
                var result = await @this.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
                await obs.OnNextAsync(result, cancellationToken);
                await obs.OnCompletedAsync(Result.Success);
            }, observer);

            return new ValueTask<IAsyncDisposable>(subscription);
        });
    }

    public static AsyncObservable<T> ToAsyncObservable<T>(this IAsyncEnumerable<T> @this)
    {
        return Create<T>((observer, _) =>
        {
            var subscription = AsyncOperationSubscription.CreateAndRun(async (obs, cancellationToken) =>
            {
                await foreach (var value in @this.WithCancellation(cancellationToken))
                {
                    await obs.OnNextAsync(value, cancellationToken);
                }

                await obs.OnCompletedAsync(Result.Success);
            }, observer);

            return new ValueTask<IAsyncDisposable>(subscription);
        });
    }

    public static AsyncObservable<T> ToAsyncObservable<T>(this IEnumerable<T> @this)
    {
        return Create<T>((observer, _) =>
        {
            var subscription = AsyncOperationSubscription.CreateAndRun(async (obs, cancellationToken) =>
            {
                foreach (var value in @this)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    await obs.OnNextAsync(value, cancellationToken);
                }

                await obs.OnCompletedAsync(Result.Success);
            }, observer);

            return new ValueTask<IAsyncDisposable>(subscription);
        });
    }
}
