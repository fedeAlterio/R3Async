using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

internal class TaskCompletionJoiner
{
    readonly TaskCompletionSource<object?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void BindTo(ValueTask task, CancellationToken cancellationToken) => BindTo(task.AsTask(), cancellationToken);
    public void BindTo(Task task, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            _tcs.TrySetResult(null);
        }
        Core();

        async void Core()
        {
            try
            {
                try
                {
                    await task.WaitAsync(timeout: Timeout.InfiniteTimeSpan,
                                         cancellationToken: cancellationToken,
                                         timeProvider: TimeProvider.System);
                }
                finally
                {
                    _tcs.TrySetResult(null);
                }
            }
            catch
            {
                // Not the place to handle this exception
            }
        }
    }

    public Task WaitCompletionAsync() => _tcs.Task;
}