# R3Async

[![NuGet](https://img.shields.io/nuget/v/R3Async.svg)](https://www.nuget.org/packages/R3Async) [![NuGet R3Interop](https://img.shields.io/nuget/v/R3Async.R3Interop.svg?label=R3Async.R3Interop)](https://www.nuget.org/packages/R3Async.R3Interop) [![codecov](https://codecov.io/gh/fedeAlterio/R3Async/branch/main/graph/badge.svg)](https://codecov.io/gh/fedeAlterio/R3Async)

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
- `TakeUntil` - Stop stream when a condition or signal occurs

#### Transformation
- `Select` - Transform values
- `Cast` - Cast to a different type
- `Scan` - Accumulate values

#### Grouping
- `GroupBy` - Group elements by key into separate observable streams

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

#### Timing
- `Debounce` - Suppress values followed by another value within a time window; only emit after a quiet period (Throttle in classic Rx.NET)
- `ThrottleFirst` - Emit the first value of each time window and drop the rest
- `ThrottleLast` - Emit only the latest value of each time window, when the window expires
- `ThrottleFirstLast` - Emit the first value of each time window immediately, then the latest value when the window expires

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

#### Subscribe Variants

Aggregation operators like `FirstAsync`, `CountAsync`, `ToListAsync`, `ToAsyncEnumerable`, etc. bundle "subscribe" and "wait for the result" into a single await, so you have no way to know exactly *when* the subscription became active - only that it happened somewhere before the result arrived. That's a problem for the common **subscribe, then trigger, then wait for the response** pattern: if you send a request before you're certain the response subscription is live, the response can arrive and be missed in the gap between sending and subscribing.

Each of these operators has a `Subscribe`-prefixed counterpart (`SubscribeFirstAsync`, `SubscribeCountAsync`, `SubscribeToListAsync`, `SubscribeToAsyncEnumerableAsync`, ...) that splits the two steps apart: it subscribes eagerly and returns as soon as the subscription is fully established, handing back a handle whose result you await separately, whenever you're ready. This lets you subscribe first, confirm the subscription is live, *then* send whatever triggers the response, with no race window:

```csharp
// 1. Subscribe first - by the time this await returns, we are guaranteed to observe
//    any matching response from this point onward.
var subscription = await responses.Values.SubscribeFirstAsync(r => r.RequestId == requestId);

// 2. Only now send the request - the response can't arrive before we're subscribed.
await SendRequestAsync(requestId);

// 3. Wait for the response (optionally bounded by a timeout/CancellationToken)
var response = await subscription.GetResultAsync(timeout: TimeSpan.FromSeconds(5));

// Or give up early without ever waiting for a response
await subscription.DisposeAsync();
```

`SubscribeToAsyncEnumerableAsync` follows the same idea for the streaming case: it returns an `IAsyncDisposableReference<IAsyncEnumerable<T>>` whose `Value` is ready to enumerate immediately and whose `DisposeAsync()` unsubscribes independently of enumeration - as opposed to `ToAsyncEnumerable`, which subscribes lazily on first `MoveNextAsync`, offering no such guarantee.

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

### TakeUntil

The `TakeUntil` operator stops emitting values from the source observable when a termination signal occurs. It supports multiple overloads for different signal types:

```csharp
// Stop when another observable emits
observable.TakeUntil(otherObservable)

// Stop when a task completes
observable.TakeUntil(task)

// Stop when cancellation token is triggered
observable.TakeUntil(cancellationToken)

// Stop when predicate returns true
observable.TakeUntil(x => x > 100)
observable.TakeUntil(async (x, ct) => await ShouldStopAsync(x, ct))

// Stop using a custom completion delegate
observable.TakeUntil(notifyStop => CreateCustomSignal(notifyStop))
```

#### TakeUntilOptions

For overloads that accept another observable, task, or completion delegate, you can configure error behavior using `TakeUntilOptions`:

```csharp
public sealed record TakeUntilOptions
{
    public bool SourceFailsWhenOtherFails { get; init; }
}
```

- **`SourceFailsWhenOtherFails = false`** (default) - If the signal source fails, the error is forwarded via `OnErrorResumeAsync` but the stream can potentially recover
- **`SourceFailsWhenOtherFails = true`** - If the signal source fails, the stream completes with `Result.Failure`, terminating the observable

```csharp
// If otherObservable fails, forward error via OnErrorResumeAsync
await observable.TakeUntil(otherObservable, new TakeUntilOptions
{
    SourceFailsWhenOtherFails = false
}).SubscribeAsync(...);

// If task fails, complete stream with failure
await observable.TakeUntil(task, new TakeUntilOptions 
{
    SourceFailsWhenOtherFails = true
}).SubscribeAsync(...);
```

#### CompletionObservableDelegate

For advanced scenarios, `TakeUntil` accepts a `CompletionObservableDelegate` that lets you integrate custom completion signals:

```csharp
public delegate IAsyncDisposable CompletionObservableDelegate(Action<Result> notifyStop);
```

Your delegate receives a callback to trigger completion and must return an `IAsyncDisposable` for cleanup:

```csharp
var observable = source.TakeUntil(notifyStop =>
{
    // Set up your signal source
    var timer = new Timer(_ => 
    {
        notifyStop(Result.Success); // Complete successfully
        // or: notifyStop(Result.Failure(exception)); // Complete with failure
    }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
    
    // Return disposable for cleanup
    return AsyncDisposable.Create(() =>
    {
        timer.Dispose();
        return default;
    });
});
```

The disposable is called when the observable completes or is disposed. This pattern allows integration with any event-based or callback-based completion mechanism.

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

For an all-in-one operator that handles multicasting, reference counting, and automatic resetting, see the `Share` operator.

### GroupBy

The `GroupBy` operator partitions an observable stream into groups based on a key selector function. Each group is represented as a `GroupedAsyncObservable<TKey, TValue>`, which is an observable that has a `Key` property identifying the group.

```csharp
public abstract class GroupedAsyncObservable<TKey, TValue> : AsyncObservable<TValue>
{
    public abstract TKey Key { get; };
}
```

#### GroupBy with Custom Subject

By default, `GroupBy` uses a regular `Subject<T>` for each group. You can provide a custom subject selector to use different subject types (e.g., `BehaviorSubject`):

```csharp
var grouped = source.GroupBy(
    keySelector: x => x % 2,
    groupSubjectSelector: key => Subject.CreateBehavior<int>(initialValue: -1)
);
```

**Important:** Each group is a hot observable that starts emitting values as soon as the source emits items for that group. Make sure to subscribe to groups promptly to avoid missing values (or use a BehaviorSubject).

### RefCountTable

`RefCountTable<TKey, TValue>` is a utility for managing a dictionary of reference-counted resources. It acts as a **message hub** or **resource registry** where resources are created on-demand and automatically cleaned up when no longer needed, preventing memory leaks.

This is particularly useful for scenarios like:
- **Message hubs**: Creating subject-based channels where consumers and producers can appear in any order
- **Shared observables**: Automatic cleanup of grouped observables when all subscribers disconnect
- **Resource pooling**: Sharing expensive resources (database connections, file handles) with automatic disposal

Similar to RabbitMQ exchange declarations, RefCountTable operations are **idempotent** - both consumers and producers can request the same key, and the resource is created once then shared.

#### Message Hub Example

A common use case is creating a subject-based message hub where consumers can subscribe before producers start publishing:

```csharp
// Create a hub of message channels (subjects) indexed by topic
var messageHub = RefCountTable.Create<string, ISubject<string>>(async (topic, ct) =>
{
    Console.WriteLine($"Creating channel for topic: {topic}");
    var subject = Subject.Create<string>();
    
    return new RefCountTable.Entry<ISubject<string>>
    {
        Value = subject,
        Disposable = AsyncDisposable.Create(async () =>
        {
            await subject.OnCompletedAsync(Result.Success);
            Console.WriteLine($"Channel '{topic}' cleaned up - all references disposed");
        })
    };
});
```

#### Consumer Before Producer (Idempotent Operations)

```csharp
// Consumer appears BEFORE producer - this is fine!
await using (var consumerRef = await messageHub.GetOrCreateAsync("orders", CancellationToken.None))
{
    // Subscribe to messages
    await using var subscription = await consumerRef.Value.Values.SubscribeAsync(
        async (msg, ct) => Console.WriteLine($"Received: {msg}")
    );
    
    // Producer appears later and gets the SAME subject
    await using (var producerRef = await messageHub.GetOrCreateAsync("orders", CancellationToken.None))
    {
        // Same subject instance
        Debug.Assert(consumerRef.Value == producerRef.Value);
        
        // Publish messages
        await producerRef.Value.OnNextAsync("Order #1", CancellationToken.None);
        await producerRef.Value.OnNextAsync("Order #2", CancellationToken.None);
        
        // Output:
        // Received: Order #1
        // Received: Order #2
        
    } // Producer reference disposed - subject still alive (consumer ref exists)
    
} // Last reference disposed - triggers cleanup
// Output: Channel 'orders' cleaned up - all references disposed
```

#### Preventing Memory Leaks with Multiple Consumers/Producers

```csharp
// Multiple consumers and producers for different topics
var hub = RefCountTable.Create<string, ISubject<int>>(async (topic, ct) =>
{
    var subject = Subject.Create<int>();
    return new RefCountTable.Entry<ISubject<int>>
    {
        Value = subject,
        Disposable = AsyncDisposable.Create(async () =>
        {
            await subject.OnCompletedAsync(Result.Success);
            Console.WriteLine($"Topic '{topic}' disposed");
        })
    };
});

// Topic "A" - 2 consumers, 1 producer
var consumerA1 = await hub.GetOrCreateAsync("A", CancellationToken.None);
var consumerA2 = await hub.GetOrCreateAsync("A", CancellationToken.None);
var producerA = await hub.GetOrCreateAsync("A", CancellationToken.None);

// Topic "B" - 1 consumer, 1 producer  
var consumerB = await hub.GetOrCreateAsync("B", CancellationToken.None);
var producerB = await hub.GetOrCreateAsync("B", CancellationToken.None);

// All references to same topic share the same subject
Debug.Assert(consumerA1.Value == consumerA2.Value);
Debug.Assert(consumerA1.Value == producerA.Value);

// Cleanup topic A (all 3 references must be disposed)
await consumerA1.DisposeAsync();  // 2 references remain for topic A
await producerA.DisposeAsync();   // 1 reference remains for topic A
await consumerA2.DisposeAsync();  // Last reference - triggers cleanup
// Output: Topic 'A' disposed

// Topic B is still active
await producerB.Value.OnNextAsync(42, CancellationToken.None);

// Cleanup topic B
await consumerB.DisposeAsync();   // 1 reference remains
await producerB.DisposeAsync();   // Last reference - triggers cleanup
// Output: Topic 'B' disposed

// No memory leaks - all subjects are properly cleaned up when no longer referenced
```

#### Reference Counting Behavior

- **First request**: Creates the resource using the factory function (idempotent - consumer or producer can be first)
- **Subsequent requests**: Returns references to the existing resource
- **Last disposal**: Automatically disposes the resource when all references are disposed (prevents memory leaks)
- **Concurrent safety**: Thread-safe for concurrent access
- **Cancellation support**: Factory function receives a `CancellationToken` for proper async cancellation

### Share

The `Share` operator allows you to share a single subscription among multiple observers. It combines the functionality of `Publish()` and `RefCount()`, while using `ShareConfig` to configure exactly when the underlying subject should be cleared and the connection reset.

```csharp
public AsyncObservable<T> Share(ShareConfig? config = null)
public AsyncObservable<T> Share(T startValue, ShareConfig? config = null)
public AsyncObservable<T> Share(Func<ISubject<T>> connector, ShareConfig? config = null)
```

`ShareConfig` has options to reset the underlying connection depending on the state of the shared subscription:

```csharp
public sealed record ShareConfig
{
    // A preconfigured config instance with all properties set to true
    public static ShareConfig ResetOnCompletionAndRefCountZero { get; }

    public bool ResetOnErrorResult { get; init; }
    public bool ResetOnSuccessResult { get; init; }
    public bool ResetOnRefCountZero { get; init; }
}
```

By default, an empty `ShareConfig` behaves exactly like `Publish().RefCount()`: when the observable completes or the final subscriber disconnects, subsequent subscribers merely receive completion results without the observable restarting.

Using `ShareConfig` with `ResetOnRefCountZero` (or `ResetOnCompletionAndRefCountZero`) is especially useful for creating hot multicast observables that automatically restart back to their original state and re-open the source once all consumers leave.

```csharp
var refCounted = source.Share(startValue: 0, ShareConfig.ResetOnCompletionAndRefCountZero);

// First subscription gets initial value and connects
await using (await refCounted.SubscribeAsync(
    async (value, ct) => Console.WriteLine($"First: {value}")
))
{
    // Output: First: 0
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

## System.IObservable Interop

The core package can convert between `System.IObservable<T>` and `AsyncObservable<T>` in both directions, with no extra package required. Since `IObservable<T>` is synchronous while `AsyncObservable<T>` is not, both directions require you to state explicitly how the sync/async boundary is handled.

### IObservable<T> → AsyncObservable<T>

`ToAsyncObservable` converts a `System.IObservable<T>` into an `AsyncObservable<T>`. Since the source pushes values synchronously while the async observer may be slow, you must choose a `BackpressureStrategy`:

```csharp
// Blocking: each notification is delivered synchronously on the emitting thread,
// which blocks until the async observer finishes processing it
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.Blocking);

// Unbounded channel: values are buffered without limit and consumed by a background loop
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel());

// Bounded channel: values are buffered up to a capacity
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromBoundedChannel(
    new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest }));

// Full control: provide the channel and how values are written to it
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromChannel(
    () => Channel.CreateBounded<int>(2),
    onNext: (value, writer) => writer.WriteAsync(value).AsTask().GetAwaiter().GetResult()));
```

Notes:

- Channel-based strategies write with `TryWrite` by default, so with a bounded channel using `FullMode = Wait` (the default) values are **silently dropped** when the buffer is full. Use a drop mode, or a custom `onNext` via `FromChannel`, to get different semantics.
- The `IObservable<T>` grammar has no resumable-error channel: `OnError` is mapped to a failure completion (`Result.Failure`), terminating the stream.

### AsyncObservable<T> → IObservable<T>

`ToSystemObservable` converts an `AsyncObservable<T>` into a `System.IObservable<T>`. `IObservable`'s `Subscribe`/`Dispose` are synchronous while R3Async's are not, so you must decide how each async operation is consumed from the sync world via an `AsyncToSyncStrategy`:

```csharp
var observable = asyncObservable.ToSystemObservable(new ToObservableConfiguration
{
    // Blocking: block the caller until the async operation completes (exceptions propagate)
    SubscribeStrategy = AsyncToSyncStrategy.Blocking,

    // FireAndForget: start the operation without waiting; failures go to the
    // optional onException callback (or the UnhandledExceptionHandler)
    DisposeStrategy = AsyncToSyncStrategy.FireAndForget(onException: ex => Console.WriteLine(ex))
});
```

Since `IObserver<T>` has no resumable-error channel, an `OnErrorResume` notification from the source is delivered as a terminal `OnError` and the subscription is torn down. A failure completion is likewise delivered as `OnError`; a success completion as `OnCompleted`.

## R3 Interop

The [R3Async.R3Interop](https://www.nuget.org/packages/R3Async.R3Interop) package bridges R3Async with [R3](https://github.com/Cysharp/R3), converting between the synchronous `Observable<T>` and the asynchronous `AsyncObservable<T>` in both directions.

```csharp
using R3Async.R3Interop;
```

The configuration types (`BackpressureStrategy`, `AsyncToSyncStrategy`, `ToObservableConfiguration`) live in the core `R3Async` package and are shared with the `System.IObservable` interop described above; the conversions work the same way in both.

### Observable<T> → AsyncObservable<T>

`ToAsyncObservable` converts an R3 observable into an R3Async one. Since an R3 source pushes values synchronously while the async observer may be slow, you must choose a `BackpressureStrategy`:

```csharp
// Blocking: each notification is delivered synchronously on the emitting thread,
// which blocks until the async observer finishes processing it
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.Blocking);

// Unbounded channel: values are buffered without limit and consumed by a background loop
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromUnboundedChannel());
var asyncObservable = observable.ToAsyncObservable(
    BackpressureStrategy.FromUnboundedChannel(new UnboundedChannelOptions { SingleReader = true }));

// Bounded channel: values are buffered up to a capacity
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromBoundedChannel(16));
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromBoundedChannel(
    new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest }));

// Full control: provide the channel and how values are written to it
var asyncObservable = observable.ToAsyncObservable(BackpressureStrategy.FromChannel(
    () => Channel.CreateBounded<int>(2),
    onNext: (value, writer) => writer.WriteAsync(value).AsTask().GetAwaiter().GetResult(),
    onErrorResume: (error, writer) => { /* forward or ignore */ }));
```

Notes:

- Channel-based strategies write with `TryWrite` by default, so with a bounded channel using `FullMode = Wait` (the default) values are **silently dropped** when the buffer is full. Use a drop mode, or a custom `onNext` via `FromChannel`, to get different semantics.
- `OnErrorResume` notifications go to the `onErrorResume` callback when one is provided (the generic `FromUnboundedChannel<T>`/`FromBoundedChannel<T>` overloads); otherwise they are routed to the `UnhandledExceptionHandler`.

### AsyncObservable<T> → Observable<T>

`ToObservable` converts an R3Async observable into an R3 one. R3's `Subscribe`/`Dispose` are synchronous while R3Async's are not, so you must decide how each async operation is consumed from the sync world via an `AsyncToSyncStrategy`:

```csharp
var observable = asyncObservable.ToObservable(new ToObservableConfiguration
{
    // Blocking: block the caller until the async operation completes (exceptions propagate)
    SubscribeStrategy = AsyncToSyncStrategy.Blocking,

    // FireAndForget: start the operation without waiting; failures go to the
    // optional onException callback (or the UnhandledExceptionHandler)
    DisposeStrategy = AsyncToSyncStrategy.FireAndForget(onException: ex => Console.WriteLine(ex))
});
```

## Missing Features

R3Async is currently under development and some features from R3 and Rx.NET are not yet implemented:

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
