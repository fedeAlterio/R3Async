using System;
using System.Diagnostics;
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

    public static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, bool startOnSubscriptionThread = false)
    {
        return CreateAsBackgroundJob(job, startOnSubscriptionThread, null);
    }

    public static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, TaskScheduler taskScheduler)
    {
        return CreateAsBackgroundJob(job, false, taskScheduler);
    }

    static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, bool startOnSubscriptionThread, TaskScheduler? taskScheduler)
    {
        if (job is null)
            throw new ArgumentNullException(nameof(job));

        return Create<T>(async (observer, token) =>
        {
            if (startOnSubscriptionThread)
            {
                Debug.Assert(taskScheduler is null);
                return CancelableTaskSubscription.CreateAndStart(job, observer);
            }

            taskScheduler ??= TaskScheduler.Default;
            return CancelableTaskSubscription.CreateAndStart(async (obs, ct) =>
            {
                await Task.Factory.StartNew(() => job(obs, ct).AsTask(),
                                            ct,
                                            TaskCreationOptions.DenyChildAttach,
                                            taskScheduler).Unwrap();
            }, observer);
        });
    }
}