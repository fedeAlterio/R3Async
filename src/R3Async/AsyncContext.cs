using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public record AsyncContext
{
    AsyncContext () {}

    public static AsyncContext From(SynchronizationContext synchronizationContext) => new()
    {
        SynchronizationContext = synchronizationContext,
        TaskScheduler = null
    };

    public static AsyncContext From(TaskScheduler taskScheduler) => new()
    {
        SynchronizationContext = null,
        TaskScheduler = taskScheduler
    };

    public static AsyncContext Default { get; } = new();

    public static AsyncContext GetCurrent()
    {
        var currentSc = SynchronizationContext.Current;
        return currentSc is not null ? From(currentSc) : From(TaskScheduler.Current);
    }

    public AsyncContextSwitcherAwaitable SwitchContextAsync(bool forceYielding, CancellationToken cancellationToken) => new(this, forceYielding, cancellationToken);

    internal bool IsDefaultContext => SynchronizationContext is null && (TaskScheduler is null || TaskScheduler == TaskScheduler.Default);
    public SynchronizationContext? SynchronizationContext { get; init; }
    public TaskScheduler? TaskScheduler { get; init; }

    public readonly struct AsyncContextSwitcherAwaitable(AsyncContext asyncContext, bool forceYielding, CancellationToken cancellationToken) : INotifyCompletion
    {
        public bool IsCompleted => !forceYielding && asyncContext.IsSameAsCurrentAsyncContext();
        public void GetResult() => cancellationToken.ThrowIfCancellationRequested();
        public AsyncContextSwitcherAwaitable GetAwaiter() => this;
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

        var currentTs = TaskScheduler.Current;
        return currentTs == TaskScheduler.Default;
    }
}