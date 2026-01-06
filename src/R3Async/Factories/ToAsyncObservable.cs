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
        return CreateAsBackgroundJob<T>(async (obs, cancellationToken) =>
        {
            var result = await @this.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
            await obs.OnNextAsync(result, cancellationToken);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }

    public static AsyncObservable<Unit> ToAsyncObservable(this Task @this)
    {
        return CreateAsBackgroundJob<Unit>(async (obs, cancellationToken) =>
        {
            await @this.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
            await obs.OnNextAsync(Unit.Default, cancellationToken);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }

    public static AsyncObservable<T> ToAsyncObservable<T>(this IAsyncEnumerable<T> @this)
    {
        return CreateAsBackgroundJob<T>(async (obs, cancellationToken) =>
        {
            await foreach (var value in @this.WithCancellation(cancellationToken))
            {
                await obs.OnNextAsync(value, cancellationToken);
            }

            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }

    public static AsyncObservable<T> ToAsyncObservable<T>(this IEnumerable<T> @this)
    {
        return CreateAsBackgroundJob<T>(async (obs, cancellationToken) =>
        {
            foreach (var value in @this)
            {
                if (cancellationToken.IsCancellationRequested) return;

                await obs.OnNextAsync(value, cancellationToken);
            }

            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }
}
