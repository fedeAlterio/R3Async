using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Create<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask<IAsyncDisposable>> subscribeAsync)
    {
        return subscribeAsync is null
            ? throw new ArgumentNullException(nameof(subscribeAsync))
            : new AnonymousAsyncObservable<T>(subscribeAsync);
    }

    public static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, bool startSynchronously = false)
    {
        return CreateAsBackgroundJob(job, startSynchronously, null);
    }

    public static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, TaskScheduler taskScheduler)
    {
        return CreateAsBackgroundJob(job, false, taskScheduler);
    }

    static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, bool startSynchronously, TaskScheduler? taskScheduler)
    {
        if (job is null)
            throw new ArgumentNullException(nameof(job));

        if (startSynchronously)
        {
            return Create<T>((observer, _) => new(CancelableTaskSubscription.CreateAndStart(job, observer)));
        }

        if (taskScheduler is null)
        {
            return Create<T>((observer, _) => new(CancelableTaskSubscription.CreateAndStart(async (obs, token) =>
            {
                await Task.Yield();
                await job(obs, token);
            }, observer)));
        }

        return Create<T>((observer, _) => new(CancelableTaskSubscription.CreateAndStart(async (obs, ct) =>
        {
            await Task.Factory.StartNew(() => job(obs, ct).AsTask(),
                                        ct,
                                        TaskCreationOptions.DenyChildAttach,
                                        taskScheduler)
                      .Unwrap();
        }, observer)));
    }
}