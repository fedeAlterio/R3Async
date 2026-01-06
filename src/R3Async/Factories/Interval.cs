using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
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
                    await using var _ = timeProvider.CreateTimer(x => ((TaskCompletionSource<bool>)x!).TrySetResult(true), tcs, period, Timeout.InfiniteTimeSpan);
                    using var __ = cancellationToken.Register(x => ((TaskCompletionSource<bool>)x!).TrySetCanceled(cancellationToken), tcs);
                    await tcs.Task;
                }

                await observer.OnNextAsync(tick++, cancellationToken);
            }
        }, true);
    }
}
