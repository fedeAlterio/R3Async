using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Runs all downstream observer calls (<c>OnNextAsync</c>, <c>OnErrorResumeAsync</c>, <c>OnCompletedAsync</c>)
        /// on the given <paramref name="asyncContext"/>. Because R3Async never uses <c>ConfigureAwait(false)</c>,
        /// this context is preserved through the rest of the operator chain: operators and the final subscriber
        /// downstream of this call all continue to execute on <paramref name="asyncContext"/>.
        /// </summary>
        /// <param name="asyncContext">The context (<see cref="SynchronizationContext"/> or <see cref="TaskScheduler"/>) to run downstream calls on.</param>
        /// <param name="forceYielding">
        /// When <see langword="true"/>, always yields and switches even if already running on <paramref name="asyncContext"/>.
        /// When <see langword="false"/> (the default), the switch is skipped if already on the target context.
        /// </param>
        public AsyncObservable<T> ObserveOn(AsyncContext asyncContext, bool forceYielding = false)
        {
            return new ObserveOnAsyncObservable<T>(@this, asyncContext, forceYielding);
        }

        /// <summary>
        /// Runs all downstream observer calls on the given <paramref name="synchronizationContext"/> by posting to it.
        /// See <see cref="ObserveOn(AsyncObservable{T}, AsyncContext, bool)"/> for context-preservation details.
        /// </summary>
        /// <param name="synchronizationContext">The synchronization context to run downstream calls on.</param>
        /// <param name="forceYielding">
        /// When <see langword="true"/>, always yields and switches even if already running on <paramref name="synchronizationContext"/>.
        /// When <see langword="false"/> (the default), the switch is skipped if already on the target context.
        /// </param>
        public AsyncObservable<T> ObserveOn(SynchronizationContext synchronizationContext, bool forceYielding = false)
        {
            var asyncContext = AsyncContext.From(synchronizationContext);
            return new ObserveOnAsyncObservable<T>(@this, asyncContext, forceYielding);
        }

        /// <summary>
        /// Runs all downstream observer calls on the given <paramref name="taskScheduler"/> by starting a task on it.
        /// See <see cref="ObserveOn(AsyncObservable{T}, AsyncContext, bool)"/> for context-preservation details.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to run downstream calls on.</param>
        /// <param name="forceYielding">
        /// When <see langword="true"/>, always yields and switches even if already running on <paramref name="taskScheduler"/>.
        /// When <see langword="false"/> (the default), the switch is skipped if already on the target context.
        /// </param>
        public AsyncObservable<T> ObserveOn(TaskScheduler taskScheduler, bool forceYielding = false)
        {
            var asyncContext = AsyncContext.From(taskScheduler);
            return new ObserveOnAsyncObservable<T>(@this, asyncContext, forceYielding);
        }
    }
}

internal sealed class ObserveOnAsyncObservable<T>(AsyncObservable<T> source, AsyncContext asyncContext, bool forceYielding) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        var observeOnObserver = new ObserveOnObserver(observer, asyncContext, forceYielding);
        return await source.SubscribeAsync(observeOnObserver, cancellationToken);
    }

    internal sealed class ObserveOnObserver(AsyncObserver<T> observer, AsyncContext asyncContext, bool forceYielding) : AsyncObserver<T>
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            await asyncContext.SwitchContextAsync(forceYielding, cancellationToken);
            await observer.OnNextAsync(value, cancellationToken);
        }

        protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            await asyncContext.SwitchContextAsync(forceYielding, cancellationToken);
            await observer.OnErrorResumeAsync(error, cancellationToken);
        }

        protected override async ValueTask OnCompletedAsyncCore(Result result)
        {
            await asyncContext.SwitchContextAsync(forceYielding, CancellationToken.None);
            await observer.OnCompletedAsync(result);
        }
    }
}
