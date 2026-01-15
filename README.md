# R3Async

[![NuGet](https://img.shields.io/nuget/v/R3Async.svg)](https://www.nuget.org/packages/R3Async)

R3Async is the **async version** of [R3](https://github.com/Cysharp/R3), a Reactive Extensions library for .NET. While R3 provides synchronous reactive programming primitives, R3Async is built from the ground up to support fully asynchronous reactive streams using `ValueTask` and `IAsyncDisposable`.

## Quick Examples

R3Async provides LINQ-style operators for composing asynchronous reactive streams:

```csharp
using R3Async;

// Filter, transform, and subscribe to an observable stream
var subscription = await AsyncObservable.Interval(TimeSpan.FromSeconds(1))
    .Where(x => x % 2 == 0)
    .Select(x => x * 10)
    .SubscribeAsync(value => Console.WriteLine($"Even value: {value}"));

// Get the first 5 items that match a condition
var result = await AsyncObservable.Interval(TimeSpan.FromMilliseconds(100))
    .Where(x => x > 3)
    .Take(5)
    .ToListAsync(CancellationToken.None);
// result: [4, 5, 6, 7, 8]

// Process async operations in sequence
var count = await AsyncObservable.CreateAsBackgroundJob<string>(async (observer, ct) =>
    {
        await observer.OnNextAsync("Hello", ct);
        await observer.OnNextAsync("World", ct);
        await observer.OnNextAsync("R3Async", ct);
        await observer.OnCompletedAsync(Result.Success);
    })
    .Select(s => s.ToUpper())
    .Do(s => Console.WriteLine(s))
    .CountAsync(CancellationToken.None);
// Prints: HELLO, WORLD, R3ASYNC
// count: 3

// Chain async transformations
var firstLong = await AsyncObservable.Return(5)
    .Select(async (x, ct) => 
    {
        await Task.Delay(100, ct);
        return x.ToString();
    })
    .Where(s => s.Length > 0)
    .FirstAsync(CancellationToken.None);
// firstLong: "5"
```

## Core Abstractions

R3Async is built on two fundamental abstractions:

### AsyncObservable<T>

The core observable type that represents an asynchronous reactive stream. It provides:

- **`SubscribeAsync`** - Subscribe to the observable stream with an observer or lambda callbacks

```csharp
public abstract class AsyncObservable<T>
{
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        AsyncObserver<T> observer, 
        CancellationToken cancellationToken);
}
```

`SubscribeAsync` also has convenient overloads that accept lambda functions instead of requiring a full observer implementation:

```csharp
// Subscribe with async lambdas for all callbacks
await observable.SubscribeAsync(
    onNextAsync: async (value, ct) => 
    {
        await ProcessValueAsync(value, ct);
        Console.WriteLine(value);
    },
    onErrorResumeAsync: async (error, ct) => 
    {
        await LogErrorAsync(error, ct);
        Console.WriteLine($"Error: {error}");
    },
    onCompletedAsync: async (result) => 
    {
        Console.WriteLine($"Completed with {result}");
    },
    cancellationToken: cancellationToken
);

// Subscribe with simple async lambda
await observable.SubscribeAsync(async (value, ct) => 
{
    Console.WriteLine(value);
}, cancellationToken);

// Subscribe with sync action
await observable.SubscribeAsync(value => Console.WriteLine(value));
```

**Important:** The `CancellationToken` parameter in `SubscribeAsync` is used only for the subscription operation itself, not for canceling the observable stream. To cancel an active subscription and stop the observable, await the `DisposeAsync()` method on the returned subscription:

```csharp
var subscription = await observable.SubscribeAsync(observer, cancellationToken);
// Later, to cancel the observable:
await subscription.DisposeAsync();
```

### AsyncObserver<T>

The observer that receives asynchronous notifications from an observable stream. It implements `IAsyncDisposable` and provides three core async methods:

- **`OnNextAsync`** - Receives the next value in the stream asynchronously
- **`OnErrorResumeAsync`** - Handles errors asynchronously (resume-based error handling)
- **`OnCompletedAsync`** - Notifies when the stream completes asynchronously

```csharp
public abstract class AsyncObserver<T> : IAsyncDisposable
{
    public ValueTask OnNextAsync(T value, CancellationToken cancellationToken);
    public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken);
    public ValueTask OnCompletedAsync(Result result);
}
```

### Key Differences from R3

- **Fully Asynchronous**: All operations return `ValueTask` instead of being synchronous
- **Cancellation Support**: Built-in `CancellationToken` support throughout the API
- **AsyncDisposable**: Uses `IAsyncDisposable` for proper async resource cleanup
- **Proper Cancellation Awaiting**: One key consequence of asynchronous support is the ability to wait for tasks to be actually canceled. For example, the `Switch` operator waits for the previous task to be fully canceled before starting the next one. In contrast, R3 and Rx.NET's `Switch` operators only initiate cancellation without waiting for completion, potentially leading to overlapping operations

## Features

### Factory Methods

Create observable streams from various sources:

- **`Create`** - Create custom observables
- **`CreateAsBackgroundJob`** - Create observables that run as background jobs, allowing proper cancellation handling and cleanup
- **`Defer`** - Defer observable creation until subscription
- **`Empty`** - Empty observable that completes immediately
- **`Never`** - Observable that never completes
- **`Return`** - Return a single value
- **`FromAsync`** - Convert async operations to observables
- **`Interval`** - Emit values at specified intervals
- **`ToAsyncObservable`** - Convert from various sources

### Operators

Transform and compose observable streams:

#### Filtering
- `Where` - Filter values based on a predicate
- `OfType` - Filter by type
- `Distinct` / `DistinctUntilChanged` - Remove duplicates
- `Skip` / `Take` - Control stream length

#### Transformation
- `Select` - Transform values
- `Cast` - Cast to a different type
- `Scan` - Accumulate values

#### Combination
- `Concat` - Concatenate sequences
- `Merge` - Merge multiple sequences
- `Switch` - Switch to latest sequence
- `Prepend` - Add values at the start
- `CombineLatest` - Combine multiple observables and emit their latest notified values

#### Error Handling
- `Catch` - Handle and recover from errors
- `Finally` - Execute cleanup logic

#### Side Effects
- `Do` - Perform side effects
- `Wrap` - Wrap observer calls

#### Concurrency & Scheduling
- `ObserveOn` - Control execution context for downstream operators

#### Multicasting
- `Multicast` - Share a single subscription to the source observable among multiple observers using a subject
- `Publish` - Multicast using a standard Subject or BehaviorSubject
- `RefCount` - Automatically connect/disconnect a connectable observable based on subscriber count

### Aggregation & Terminal Operations

Async methods that consume the observable and return results:

- `FirstAsync` / `FirstOrDefaultAsync` - Get first element
- `LastAsync` / `LastOrDefaultAsync` - Get last element
- `SingleAsync` / `SingleOrDefaultAsync` - Get single element
- `AnyAsync` / `AllAsync` - Test conditions
- `ContainsAsync` - Check for element
- `CountAsync` / `LongCountAsync` - Count elements
- `ForEachAsync` - Execute action for each element
- `ToListAsync` - Collect to list
- `ToDictionaryAsync` - Collect to dictionary
- `ToAsyncEnumerable` - Convert to async enumerable using System.Threading.Channels

#### ToAsyncEnumerable and Channel Selection

There is no "one way" to convert an async observable to an async enumerable - the behavior depends on backpressure semantics. For this reason, `ToAsyncEnumerable` accepts a channel factory function, allowing you to choose the appropriate channel type:

```csharp
// Rendezvous channel (capacity = 0) - strict backpressure
// Producer waits until consumer reads each item
await foreach (var x in observable.ToAsyncEnumerable(() => Channel.CreateBounded<int>(0)))
{
    // Process item
}

// Bounded channel - limited backpressure buffer
await foreach (var x in observable.ToAsyncEnumerable(() => Channel.CreateBounded<int>(10)))
{
    // Process item - producer can stay up to 10 items ahead
}

// Unbounded channel - no backpressure
// Producer never waits, all items are buffered
await foreach (var x in observable.ToAsyncEnumerable(() => Channel.CreateUnbounded<int>()))
{
    // Process item
}
```

Channels already encode the desired conversion semantics, so you have full control over buffering and backpressure behavior.

### ObserveOn and AsyncContext

The `ObserveOn` operator controls the async context for downstream operators. R3Async's `ObserveOn` is based on actual async behavior in .NET, leveraging `SynchronizationContext` and `TaskScheduler`.

#### AsyncContext

`AsyncContext` is a discriminated union that encapsulates either a `SynchronizationContext` or a `TaskScheduler`:

```csharp
// Create from SynchronizationContext
var context = AsyncContext.From(SynchronizationContext.Current);

// Create from TaskScheduler
var context = AsyncContext.From(TaskScheduler.Current);

// Get the current context
var context = AsyncContext.GetCurrent();
```

`AsyncContext` provides a utility method `SwitchContextAsync()` that returns an awaitable. When awaited, it runs the continuation on the actual context (either the `SynchronizationContext` or `TaskScheduler`):

```csharp
var context = AsyncContext.From(uiSyncContext);
await context.SwitchContextAsync(forceYielding: false, cancellationToken);
// Code after this point executes on the UI context
```

When you call `ObserveOn(asyncContext)`, all downstream observer calls (`OnNextAsync`, `OnErrorResumeAsync`, `OnCompletedAsync`) will be executed on that context - either by posting to the `SynchronizationContext` or starting a task on the `TaskScheduler`.

#### Context Preservation

A fundamental property of `ObserveOn` in R3Async is that it **does not lose context**. Because the implementation never uses `ConfigureAwait(false)`, when you chain operators after `ObserveOn`, they continue to execute on the specified async context:

```csharp
await observable
    .ObserveOn(uiContext)        // Switch to UI context
    .Select(async (x, ct) => await Something(x, ct))           // Still executes on UI context
    .Where(x => x > 10)           // Still executes on UI context
    .SubscribeAsync(value =>      // Still executes on UI context
    {
        uiControl.Text = value.ToString(); // Safe to update UI
    });
```

This behavior is similar to how synchronous Rx's `ObserveOn` works, where the scheduler context flows through the entire chain of downstream operators.

#### Force Yielding

The `forceYielding` parameter controls whether `ObserveOn` always yields execution, even if already on the target context:

```csharp
// Only switch if not already on the context
observable.ObserveOn(context, forceYielding: false)

// Always yield, even if already on the context
observable.ObserveOn(context, forceYielding: true)
```

### Subjects

Hot observables that can be controlled imperatively:

```csharp
public interface ISubject<T>
{
    AsyncObservable<T> Values { get; }
    ValueTask OnNextAsync(T value, CancellationToken cancellationToken);
    ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken);
    ValueTask OnCompletedAsync(Result result);
}
```

#### Subject

Subjects can be created using the static `Subject.Create<T>()` factory method with optional creation options:

```csharp
// Create with default options (Serial publishing)
var subject = Subject.Create<int>();

// Create with explicit options
var concurrentSubject = Subject.Create<string>(new SubjectCreationOptions
{
    PublishingOption = PublishingOption.Concurrent
});
```

**Publishing Options:**
- **`PublishingOption.Serial`** (default) - Observers are notified serially, one after another
- **`PublishingOption.Concurrent`** - Observers are notified concurrently, allowing parallel execution

Once created, push values through the subject and subscribe to its `Values` observable:

```csharp
var subject = Subject.Create<int>();

// Subscribe to the subject
await using var subscription = await subject.Values.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"Received: {value}")
);

// Push values
await subject.OnNextAsync(1, CancellationToken.None);
await subject.OnNextAsync(2, CancellationToken.None);
await subject.OnCompletedAsync(Result.Success);
```

#### BehaviorSubject

BehaviorSubject is a type of subject that stores the latest value and emits it to new subscribers immediately upon subscription. It can be created using the static `Subject.CreateBehavior<T>()` factory method:

```csharp
// Create with initial value and default options (Serial publishing)
var behaviorSubject = Subject.CreateBehavior<int>(0);

// Create with explicit options
var concurrentBehaviorSubject = Subject.CreateBehavior<string>("initial", new BehaviorSubjectCreationOptions
{
    PublishingOption = PublishingOption.Concurrent
});
```

The BehaviorSubject stores the latest emitted value and immediately sends it to new subscribers:

```csharp
var subject = Subject.CreateBehavior<int>(42);

// First subscriber receives the initial value (42)
await using var sub1 = await subject.Values.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"Sub1: {value}")
);
// Output: Sub1: 42

// Emit new values
await subject.OnNextAsync(100, CancellationToken.None);
// Output: Sub1: 100

await subject.OnNextAsync(200, CancellationToken.None);
// Output: Sub1: 200

// New subscriber receives the latest value (200) immediately
await using var sub2 = await subject.Values.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"Sub2: {value}")
);
// Output: Sub2: 200

// Subsequent values are sent to all subscribers
await subject.OnNextAsync(300, CancellationToken.None);
// Output: Sub1: 300
// Output: Sub2: 300
```

### Multicast and Publish

Multicast operators allow you to share a single subscription to the source observable among multiple observers. This is useful for "hot" observables where you want to avoid re-executing the source logic for each subscriber.

#### ConnectableAsyncObservable

The `Multicast` and `Publish` operators return a `ConnectableAsyncObservable<T>`, which has two key methods:

- **`SubscribeAsync`** - Subscribe observers to the connectable observable (same as regular AsyncObservable)
- **`ConnectAsync`** - Connect to the source observable and start multicasting values to all subscribers

```csharp
public abstract class ConnectableAsyncObservable<T> : AsyncObservable<T>
{
    public abstract ValueTask<IAsyncDisposable> ConnectAsync(CancellationToken cancellationToken);
}
```

**Important:** Subscribers will not receive values until `ConnectAsync` is called. The connection can be disposed to stop the source subscription.

#### Multicast

The `Multicast` operator converts a cold observable into a hot connectable observable using a subject:

```csharp
var source = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, ct) =>
{
    await observer.OnNextAsync(1, ct);
    await observer.OnNextAsync(2, ct);
    await observer.OnNextAsync(3, ct);
    await observer.OnCompletedAsync(Result.Success);
});

var subject = Subject.Create<int>();
var multicast = source.Multicast(subject);

// Subscribe multiple observers
await using var sub1 = await multicast.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"Observer 1: {value}")
);

await using var sub2 = await multicast.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"Observer 2: {value}")
);

// Connect to start receiving values
await using var connection = await multicast.ConnectAsync(CancellationToken.None);

// Both observers receive all values from the single source subscription
// Output:
// Observer 1: 1
// Observer 2: 1
// Observer 1: 2
// Observer 2: 2
// Observer 1: 3
// Observer 2: 3
```

#### Publish

The `Publish` operator is a convenience method that calls `Multicast` with a new Subject:

```csharp
// These are equivalent:
var multicast1 = source.Multicast(Subject.Create<int>());
var multicast2 = source.Publish();

// Publish with options
var multicast3 = source.Publish(new SubjectCreationOptions
{
    PublishingOption = PublishingOption.Concurrent
});

// Publish with BehaviorSubject (provides initial value)
var multicast4 = source.Publish(initialValue: 0);

// Publish with BehaviorSubject and options
var multicast5 = source.Publish(initialValue: 0, new BehaviorSubjectCreationOptions
{
    PublishingOption = PublishingOption.Serial
});
```

#### RefCount

The `RefCount` operator automatically manages connections to a `ConnectableAsyncObservable` based on the number of subscribers. When the first subscriber subscribes, it connects to the source. When the last subscriber unsubscribes, it disconnects.

RefCount is particularly useful with stateless subjects to create observables that automatically reset when all observers unsubscribe.

### Stateless Subjects

Stateless subjects are a variant of subjects that automatically reset their state when all observers unsubscribe. This is useful for creating reusable hot observables that can be "restarted" without creating a new instance.

#### Stateless Subject vs Regular Subject

- **Regular Subject**: Once completed, it stays completed. New subscribers immediately receive the completion notification.
- **Stateless Subject**: Forgets completion when all observers unsubscribe. After reset, it acts as a fresh proxy that can receive and forward new values.


#### Stateless BehaviorSubject vs Regular BehaviorSubject

- **Regular BehaviorSubject**: Stores the latest value permanently.
- **Stateless BehaviorSubject**: Returns to its original initial value when all observers unsubscribe.


Stateless subjects are particularly useful with `RefCount` for creating auto-resetting multicast observables:

```csharp
var source = Subject.Create<int>();
var refCounted = source.Values.StatelessPublish(initialValue: 0).RefCount();

// First subscription gets initial value and connects
await using (await refCounted.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"First: {value}")
))
{
    // Output: First: 0
    await source.OnNextAsync(10, CancellationToken.None);
    // Output: First: 10
}
// All observers unsubscribed - disconnects and resets to initial value

// New subscription reconnects and gets initial value again
await using var sub = await refCounted.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"Second: {value}")
);
// Output: Second: 0
```


### Disposables

Async disposable utilities for resource management:

- `AsyncDisposable` - Create custom async disposables
- `CompositeAsyncDisposable` - Dispose multiple resources together
- `SerialAsyncDisposable` - Replace disposables serially
- `SingleAssignmentDisposable` - Single assignment semantics

## Usage Example

```csharp
using R3Async;

// Create an observable from an async enumerable
var observable = AsyncObservable.Create<int>(async (observer, ct) =>
{
    await observer.OnNextAsync(1, ct);
    await observer.OnNextAsync(2, ct);
    await observer.OnNextAsync(3, ct);
    await observer.OnCompletedAsync(Result.Success);
    return AsyncDisposable.Empty;
});

// Subscribe and process values
await using var subscription = await observable
    .Where(x => x % 2 == 0)
    .Select(x => x * 10)
    .SubscribeAsync(async (value, ct) =>
    {
        Console.WriteLine($"Received: {value}");
        await Task.CompletedTask;
    });

// Using a Subject
var subject = new Subject<string>();

await using var sub = await subject.Values.SubscribeAsync(
    async (value, ct) => Console.WriteLine(value)
);

await subject.OnNextAsync("Hello", CancellationToken.None);
await subject.OnNextAsync("World", CancellationToken.None);
await subject.OnCompletedAsync(Result.Success);
```

### Background Jobs with AsyncEnumerable Interop

R3Async provides advanced features for background processing with proper cancellation handling and backpressure control using System.Threading.Channels:

```csharp
using System.Threading.Channels;
using R3Async;

// Create a background job observable that properly handles cancellation
var obs = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
{
    try
    {
        var i = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await observer.OnNextAsync(i++, token);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Canceling");
        // Simulate cleanup work
        await Task.Delay(2000);
        Console.WriteLine("Canceled");
        throw;
    }
});

// Convert to async enumerable with bounded channel for backpressure
await foreach (var x in obs.ToAsyncEnumerable(() => Channel.CreateBounded<int>(0)))
{
    Console.WriteLine($"Consumed {x}");
    var line = Console.ReadLine();
    if (line == "exit")
        break;
}

Console.WriteLine("Exited");
```

This example demonstrates:
- **CreateAsBackgroundJob** - Creates an observable that runs in the background
- **Channel-based backpressure** - Using `Channel.CreateBounded<int>(0)` ensures the producer waits when the consumer is slow
- **Graceful cancellation** - When the consumer breaks, the producer can perform cleanup before fully terminating. _Exited_ is printed after _Canceled_

## Concurrency Protection

R3Async includes built-in protection against concurrent observer calls. Concurrent calls to `OnNextAsync`, `OnErrorResumeAsync`, or `OnCompletedAsync` on the same observer instance will route a `ConcurrentObserverCallsException` to the UnhandledExceptionHandler (they don't stop the observable chain).

## Unhandled Exception Handling

By default, unhandled exceptions in R3Async are written to the console. You can customize this behavior by registering a custom handler:

```csharp
UnhandledExceptionHandler.Register(exception =>
{
    // Custom exception handling logic
    MyLogger.LogError(exception);
});
```

Note: `OperationCanceledException` is automatically ignored by the unhandled exception handler.

## Missing Features

R3Async is currently under development and some features from R3 and Rx.NET are not yet implemented:

- **Throttle / Debounce** - Time-based filtering operators
- **Zip** - Combine multiple observables pairwise
- **Race (Amb)** - Return the first observable to emit
- **Others..**

### Design Decisions

- **No ConfigureAwait(false)** - By design, R3Async does not use `ConfigureAwait(false)`. This is a deliberate choice to maintain context flow and avoid potential issues with context loss. For more context on this decision, see [dotnet/runtime#113567](https://github.com/dotnet/runtime/issues/113567) and [dotnet/reactive#1967](https://github.com/dotnet/reactive/discussions/1967). This design choice is particularly important for `ObserveOn`, which preserves execution context throughout the operator chain.

These features may be added in future releases.
## Related Projects

- [R3](https://github.com/Cysharp/R3) - The synchronous Reactive Extensions library that R3Async is based on

## License

See [LICENSE](LICENSE) file in the repository.
