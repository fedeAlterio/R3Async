using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{Int64}"/> that emits an incrementing tick count (starting at 1) after every
    /// <paramref name="period"/>, indefinitely, until the subscription is disposed. Never completes on its own.
    /// </summary>
    /// <param name="period">The delay between consecutive ticks.</param>
    /// <param name="timeProvider">
    /// The <see cref="TimeProvider"/> used to schedule ticks. When <c>null</c> or <see cref="TimeProvider.System"/>,
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> is used directly.
    /// </param>
    public static AsyncObservable<long> Interval(TimeSpan period, TimeProvider? timeProvider = null)
    {
        return CreateAsBackgroundJob<long>(async (observer, cancellationToken) =>
        {
            long tick = 1;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (timeProvider is null || timeProvider == TimeProvider.System)
                {
                    await Task.Delay(period, cancellationToken);
                }
                else
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await using var _ = timeProvider.CreateTimer(x => ((TaskCompletionSource<bool>)x!).TrySetResult(true), tcs, period, System.Threading.Timeout.InfiniteTimeSpan);
                    using var __ = cancellationToken.Register(x => ((TaskCompletionSource<bool>)x!).TrySetCanceled(cancellationToken), tcs);
                    await tcs.Task;
                }

                await observer.OnNextAsync(tick++, cancellationToken);
            }
        }, true);
    }
}
