using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> from a custom subscription function. The function is invoked once per
    /// subscription with the subscribing observer and a <see cref="CancellationToken"/> for the subscribe operation itself,
    /// and must return an <see cref="IAsyncDisposable"/> that unsubscribes and releases any resources when disposed.
    /// </summary>
    public static AsyncObservable<T> Create<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask<IAsyncDisposable>> subscribeAsync)
    {
        return subscribeAsync is null
            ? throw new ArgumentNullException(nameof(subscribeAsync))
            : new AnonymousAsyncObservable<T>(subscribeAsync);
    }

    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> whose subscription runs <paramref name="job"/> as a cancelable background
    /// task instead of directly on the subscribing call. Disposing the resulting subscription cancels the job and waits
    /// for it to fully observe the cancellation before completing, so cleanup performed by <paramref name="job"/> after
    /// catching <see cref="OperationCanceledException"/> is guaranteed to run to completion.
    /// </summary>
    /// <param name="job">The background job that drives the observer, invoked with the observer and a token canceled on unsubscribe.</param>
    /// <param name="startSynchronously">
    /// When <c>true</c>, the job starts executing synchronously on the calling thread until its first await; when
    /// <c>false</c> (the default), the job starts after yielding, so subscription always returns before the job runs.
    /// </param>
    public static AsyncObservable<T> CreateAsBackgroundJob<T>(Func<AsyncObserver<T>, CancellationToken, ValueTask> job, bool startSynchronously = false)
    {
        return CreateAsBackgroundJob(job, startSynchronously, null);
    }

    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> whose subscription runs <paramref name="job"/> as a cancelable background
    /// task scheduled via the given <see cref="TaskScheduler"/>. Disposing the resulting subscription cancels the job and
    /// waits for it to fully observe the cancellation before completing.
    /// </summary>
    /// <param name="job">The background job that drives the observer, invoked with the observer and a token canceled on unsubscribe.</param>
    /// <param name="taskScheduler">The scheduler used to run the job.</param>
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