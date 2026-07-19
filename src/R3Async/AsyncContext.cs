using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// A discriminated union wrapping either a <see cref="SynchronizationContext"/> or a <see cref="TaskScheduler"/>,
/// used by <c>ObserveOn</c> to describe the execution context downstream observer calls should run on.
/// </summary>
public record AsyncContext
{
    AsyncContext () {}

    /// <summary>Creates an <see cref="AsyncContext"/> that posts continuations to <paramref name="synchronizationContext"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="synchronizationContext"/> is <see langword="null"/>.</exception>
    public static AsyncContext From(SynchronizationContext synchronizationContext)
    {
        if (synchronizationContext is null)
            throw new ArgumentNullException(nameof(synchronizationContext));

        return new()
        {
            SynchronizationContext = synchronizationContext,
            TaskScheduler = null
        };
    }

    /// <summary>Creates an <see cref="AsyncContext"/> that schedules continuations on <paramref name="taskScheduler"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="taskScheduler"/> is <see langword="null"/>.</exception>
    public static AsyncContext From(TaskScheduler taskScheduler)
    {
        if (taskScheduler is null)
            throw new ArgumentNullException(nameof(taskScheduler));

        return new()
        {
            SynchronizationContext = null,
            TaskScheduler = taskScheduler
        };
    }

    /// <summary>Gets the default <see cref="AsyncContext"/>: no <see cref="SynchronizationContext"/> and the default <see cref="TaskScheduler"/>.</summary>
    public static AsyncContext Default { get; } = new();

    /// <summary>
    /// Captures the calling thread's current context: <see cref="SynchronizationContext.Current"/> if one is set,
    /// otherwise <see cref="TaskScheduler.Current"/>.
    /// </summary>
    public static AsyncContext GetCurrent()
    {
        var currentSc = SynchronizationContext.Current;
        return currentSc is not null ? From(currentSc) : From(TaskScheduler.Current);
    }

    /// <summary>
    /// Returns an awaitable that, when awaited, resumes execution on this context (via
    /// <see cref="SynchronizationContext.Post"/> or by starting a task on the <see cref="TaskScheduler"/>).
    /// </summary>
    /// <param name="forceYielding">
    /// If <see langword="true"/>, always yields and reschedules the continuation even if execution is already on
    /// this context. If <see langword="false"/>, the switch is skipped when already on this context.
    /// </param>
    /// <param name="cancellationToken">
    /// Checked when the continuation resumes; if canceled, the awaiter throws <see cref="OperationCanceledException"/>.
    /// </param>
    public AsyncContextSwitcherAwaitable SwitchContextAsync(bool forceYielding, CancellationToken cancellationToken) => new(this, forceYielding, cancellationToken);

    internal bool IsDefaultContext => SynchronizationContext is null && (TaskScheduler is null || TaskScheduler == TaskScheduler.Default);

    /// <summary>The <see cref="SynchronizationContext"/> to switch to, or <see langword="null"/> if this instance wraps a <see cref="TaskScheduler"/> instead.</summary>
    public SynchronizationContext? SynchronizationContext { get; init; }

    /// <summary>The <see cref="TaskScheduler"/> to switch to, or <see langword="null"/> if this instance wraps a <see cref="SynchronizationContext"/> instead.</summary>
    public TaskScheduler? TaskScheduler { get; init; }

    /// <summary>The awaitable type returned by <see cref="SwitchContextAsync"/>.</summary>
    public readonly struct AsyncContextSwitcherAwaitable(AsyncContext asyncContext, bool forceYielding, CancellationToken cancellationToken) : INotifyCompletion
    {
        /// <summary>Gets whether the switch can complete synchronously (not forced to yield, and already on the target context).</summary>
        public bool IsCompleted => !forceYielding && asyncContext.IsSameAsCurrentAsyncContext();

        /// <summary>Completes the await, throwing if the cancellation token was canceled.</summary>
        public void GetResult() => cancellationToken.ThrowIfCancellationRequested();

        /// <summary>Returns this instance as its own awaiter.</summary>
        public AsyncContextSwitcherAwaitable GetAwaiter() => this;

        /// <inheritdoc/>
        public void OnCompleted(Action continuation)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                continuation();
                return;
            }

            var sc = asyncContext.SynchronizationContext;
            if (sc is not null)
            {
                sc.Post(c => ((Action)c!).Invoke(),continuation);
                return;
            }

            var ts = asyncContext.TaskScheduler ?? TaskScheduler.Default;
            Task.Factory.StartNew(continuation, CancellationToken.None, TaskCreationOptions.DenyChildAttach, ts);
        }
    }
}

internal static class AsyncContextEx
{
    public static bool IsSameAsCurrentAsyncContext(this AsyncContext @this)
    {
        if (@this.SynchronizationContext is not null)
        {
            return @this.SynchronizationContext == SynchronizationContext.Current;
        }

        if (@this.TaskScheduler is not null)
        {
            return @this.TaskScheduler == TaskScheduler.Current;
        }

        return TaskScheduler.Current == TaskScheduler.Default;
    }
}