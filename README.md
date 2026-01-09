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

#### Creating Subjects

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

- **BehaviorSubject** - Subject that stores and emits the latest value to new subscribers
- **Publish / IConnectableObservable** - Hot observable multicasting support
- **Throttle / Debounce** - Time-based filtering operators
- **Zip** - Combine multiple observables pairwise
- **Race (Amb)** - Return the first observable to emit
- **Others..**
- **ObserveOn** - The concept of schedulers in an async context requires further design consideration. Since async/await already works with TaskScheduler and SynchronizationContext, it's unclear whether TimeProvider-based scheduling or custom schedulers would provide meaningful value in this context.

### Design Decisions

- **No ConfigureAwait(false)** - By design, R3Async does not use `ConfigureAwait(false)`. This is a deliberate choice to maintain context flow and avoid potential issues with context loss. For more context on this decision, see [dotnet/runtime#113567](https://github.com/dotnet/runtime/issues/113567) and [dotnet/reactive#1967](https://github.com/dotnet/reactive/discussions/1967).

These features may be added in future releases.
## Related Projects

- [R3](https://github.com/Cysharp/R3) - The synchronous Reactive Extensions library that R3Async is based on

## License

See [LICENSE](LICENSE) file in the repository.
