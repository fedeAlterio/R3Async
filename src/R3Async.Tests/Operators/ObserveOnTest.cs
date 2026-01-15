using System.Collections.Concurrent;
using Shouldly;
#pragma warning disable CS1998

namespace R3Async.Tests.Operators;

public class ObserveOnTest
{
    [Fact]
    public async Task ObserveOn_Default_DoesNotChangeContext()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        await completedTcs.Task;

        results.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ObserveOn_ForceYielding_YieldsOnDefault()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default, forceYielding: true);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        await completedTcs.Task;

        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ObserveOn_TaskScheduler_SwitchesContext()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedSchedulers = new List<TaskScheduler?>();
        
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            capturedSchedulers.Add(TaskScheduler.Current);
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var customScheduler = new CustomTaskScheduler();
        var asyncContext = AsyncContext.From(customScheduler);
        var observeOnObservable = observable.ObserveOn(asyncContext);
        
        var observerSchedulers = new List<TaskScheduler?>();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) =>
            {
                observerSchedulers.Add(TaskScheduler.Current);
                results.Add(x);
            },
            async (ex, token) => { },
            async result =>
            {
                observerSchedulers.Add(TaskScheduler.Current);
                completedTcs.TrySetResult(result.IsSuccess);
            },
            CancellationToken.None);

        await tcs.Task;
        await completedTcs.Task;

        results.ShouldBe(new[] { 1, 2 });
        observerSchedulers.ShouldAllBe(s => s == customScheduler);
    }

    [Fact]
    public async Task ObserveOn_SynchronizationContext_SwitchesContext()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var customSyncContext = new CustomSynchronizationContext();
        var asyncContext = AsyncContext.From(customSyncContext);
        var observeOnObservable = observable.ObserveOn(asyncContext);
        
        var observerSyncContexts = new List<SynchronizationContext?>();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) =>
            {
                observerSyncContexts.Add(SynchronizationContext.Current);
                results.Add(x);
            },
            async (ex, token) => { },
            async result =>
            {
                observerSyncContexts.Add(SynchronizationContext.Current);
                completedTcs.TrySetResult(result.IsSuccess);
            },
            CancellationToken.None);

        await tcs.Task;
        await completedTcs.Task;

        results.ShouldBe(new[] { 1, 2 });
        observerSyncContexts.ShouldAllBe(sc => sc == customSyncContext);
        customSyncContext.PostCallCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ObserveOn_SingleThreadedSynchronizationContext_EnsuresSequentialExecution()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnNextAsync(2, token);
            await observer.OnNextAsync(3, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        using var singleThreadedContext = new SingleThreadedSynchronizationContext();
        var asyncContext = AsyncContext.From(singleThreadedContext);
        var observeOnObservable = observable.ObserveOn(asyncContext);
        
        var threadIds = new List<int>();
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) =>
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
                results.Add(x);
                await Task.Yield(); // Simulate some async work
                threadIds.Add(Environment.CurrentManagedThreadId);
            },
            async (ex, token) => { },
            async result =>
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
                completedTcs.TrySetResult(result.IsSuccess);
            },
            CancellationToken.None);

        await tcs.Task;
        await completedTcs.Task;

        results.ShouldBe(new[] { 1, 2, 3 });
        // All callbacks should execute on the same thread (single-threaded context)
        threadIds.Distinct().Count().ShouldBe(1);
        // Verify the thread ID is the single-threaded context's thread
        threadIds.First().ShouldBe(singleThreadedContext.ThreadId);
    }

    [Fact]
    public async Task ObserveOn_ErrorPropagation()
    {
        var expected = new InvalidOperationException("test error");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnErrorResumeAsync(expected, token);
            await observer.OnNextAsync(2, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default);
        var results = new List<int>();
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => errorTcs.TrySetResult(ex),
            async result => completedTcs.TrySetResult(result.IsSuccess),
            CancellationToken.None);

        await tcs.Task;
        var error = await errorTcs.Task;
        await completedTcs.Task;

        error.ShouldBe(expected);
        results.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task ObserveOn_FailureCompletion()
    {
        var expected = new InvalidOperationException("completion error");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            await observer.OnCompletedAsync(Result.Failure(expected));
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => { if (result.IsFailure) completedTcs.TrySetResult(result.Exception); },
            CancellationToken.None);

        await tcs.Task;
        var error = await completedTcs.Task;

        error.ShouldBe(expected);
        results.ShouldBe(new[] { 1 });
    }

    [Fact]
    public async Task ObserveOn_Disposal()
    {
        var disposed = false;
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnNextAsync(1, token);
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default);
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) => tcs.TrySetResult(x), 
            CancellationToken.None);
        
        await tcs.Task;
        await subscription.DisposeAsync();
        
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ObserveOn_ReentranceDispose()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            _ = Task.Run(async () =>
            {
                await tcs.Task;
                await observer.OnNextAsync(1, token);
                await observer.OnNextAsync(2, token);
                await observer.OnNextAsync(3, token);
                completedTcs.TrySetResult();
            });
            return AsyncDisposable.Create(() =>
            {
                disposed = true;
                return default;
            });
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default);
        var results = new List<int>();
        IAsyncDisposable? subscription = null;
        
        subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) =>
            {
                results.Add(x);
                await subscription!.DisposeAsync();
            },
            CancellationToken.None);
        
        tcs.TrySetResult();
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 1 });
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ObserveOn_EmptyObservable()
    {
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            await observer.OnCompletedAsync(Result.Success);
            return AsyncDisposable.Empty;
        });

        var observeOnObservable = observable.ObserveOn(AsyncContext.Default);
        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await observeOnObservable.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(true),
            CancellationToken.None);
        
        await completedTcs.Task;
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task ObserveOn_ChainedWithOtherOperators()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = AsyncObservable.Create<int>(async (observer, token) =>
        {
            for (int i = 1; i <= 5; i++)
                await observer.OnNextAsync(i, token);
            await observer.OnCompletedAsync(Result.Success);
            tcs.TrySetResult();
            return AsyncDisposable.Empty;
        });

        var result = observable
            .Where(x => x > 2)
            .ObserveOn(AsyncContext.Default)
            .Select(x => x * 10);

        var results = new List<int>();
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await result.SubscribeAsync(
            async (x, token) => results.Add(x),
            async (ex, token) => { },
            async result => completedTcs.TrySetResult(true),
            CancellationToken.None);
        
        await tcs.Task;
        await completedTcs.Task;
        
        results.ShouldBe(new[] { 30, 40, 50 });
    }

    private class CustomTaskScheduler : TaskScheduler
    {
        protected override IEnumerable<Task> GetScheduledTasks() => Enumerable.Empty<Task>();

        protected override void QueueTask(Task task)
        {
            Task.Run(() => TryExecuteTask(task));
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }
    }

    private class CustomSynchronizationContext : SynchronizationContext
    {
        private int _postCallCount;
        public int PostCallCount => _postCallCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCallCount);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                try
                {
                    d(state);
                }
                finally
                {
                    SetSynchronizationContext(null);
                }
            });
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }

    private class SingleThreadedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback callback, object? state)> _queue = new();
        private readonly Thread _thread;
        private readonly int _threadId;
        private bool _disposed;

        public int ThreadId => _threadId;

        public SingleThreadedSynchronizationContext()
        {
            _thread = new Thread(RunOnCurrentThread)
            {
                IsBackground = true,
                Name = "SingleThreadedSyncContext"
            };
            _thread.Start();
            
            // Wait for thread to start and capture its ID
            var tcs = new TaskCompletionSource<int>();
            Post(_ => tcs.TrySetResult(Environment.CurrentManagedThreadId), null);
            _threadId = tcs.Task.Result;
        }

        private void RunOnCurrentThread()
        {
            SetSynchronizationContext(this);
            try
            {
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            }
            finally
            {
                SetSynchronizationContext(null);
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_disposed)
                return;
            
            _queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == _threadId)
            {
                d(state);
            }
            else
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Post(_ =>
                {
                    try
                    {
                        d(state);
                        tcs.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, null);
                tcs.Task.Wait();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _queue.CompleteAdding();
            if (!_thread.Join(TimeSpan.FromSeconds(5)))
            {
                // Thread didn't stop in time, but we're disposing anyway
            }
            _queue.Dispose();
        }
    }
}
