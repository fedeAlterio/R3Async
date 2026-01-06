using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals;

internal static class TaskExtensions
{
    public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<TResult>();
        using (new Timer(s => ((TaskCompletionSource<TResult>)s!).TrySetException(new TimeoutException()), tcs, timeout, Timeout.InfiniteTimeSpan))
        using (cancellationToken.Register(s => ((TaskCompletionSource<TResult>)s!).TrySetCanceled(), tcs))
        {
            return await (await Task.WhenAny(task, tcs.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (new Timer(s => ((TaskCompletionSource<bool>)s!).TrySetException(new TimeoutException()), tcs, timeout, Timeout.InfiniteTimeSpan))
        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), tcs))
        {
            await (await Task.WhenAny(task, tcs.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}