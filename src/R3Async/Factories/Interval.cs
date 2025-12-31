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
        static async IAsyncEnumerable<long> PeriodicTimerImpl(TimeSpan period, TimeProvider timeProvider, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            long tick = 1;
            while (!cancellationToken.IsCancellationRequested)
            {
                await timeProvider.Delay(period, cancellationToken);
                yield return tick++;
            }
        }

        return PeriodicTimerImpl(period, timeProvider ?? TimeProvider.System).ToAsyncObservable();
    }
}
