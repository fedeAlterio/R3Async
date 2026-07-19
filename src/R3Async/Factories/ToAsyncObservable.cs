using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Converts a <see cref="Task{T}"/> into an <see cref="AsyncObservable{T}"/> that awaits the task on subscription
    /// (as a cancelable background job) and emits its result as the single value before completing successfully.
    /// Disposing the subscription before the task completes cancels the wait.
    /// </summary>
    public static AsyncObservable<T> ToAsyncObservable<T>(this Task<T> @this)
    {
        return CreateAsBackgroundJob<T>(async (obs, cancellationToken) =>
        {
            var result = await @this.WaitAsync(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            await obs.OnNextAsync(result, cancellationToken);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }

    /// <summary>
    /// Converts a <see cref="Task"/> into an <see cref="AsyncObservable{Unit}"/> that awaits the task on subscription
    /// (as a cancelable background job) and emits <see cref="Unit.Default"/> before completing successfully.
    /// Disposing the subscription before the task completes cancels the wait.
    /// </summary>
    public static AsyncObservable<Unit> ToAsyncObservable(this Task @this)
    {
        return CreateAsBackgroundJob<Unit>(async (obs, cancellationToken) =>
        {
            await @this.WaitAsync(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            await obs.OnNextAsync(Unit.Default, cancellationToken);
            await obs.OnCompletedAsync(Result.Success);
        }, true);
    }

    /// <summary>
    /// Converts an <see cref="IAsyncEnumerable{T}"/> into an <see cref="AsyncObservable{T}"/> that, on subscription,
    /// enumerates the source as a cancelable background job, emitting each item in order and completing successfully
    /// once enumeration finishes. Disposing the subscription cancels the enumeration.
    /// </summary>
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

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}"/> into an <see cref="AsyncObservable{T}"/> that, on subscription,
    /// iterates the source as a cancelable background job, emitting each item in order and completing successfully
    /// once iteration finishes. If the subscription is canceled mid-iteration, emission simply stops without
    /// invoking <c>OnCompleted</c>.
    /// </summary>
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
