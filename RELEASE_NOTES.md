## What's New

### GroupBy Operator
Partitions observable streams into groups based on a key selector. Each group is a `GroupedAsyncObservable<TKey, TValue>` with a `Key` property.

```csharp
var grouped = source.GroupBy(x => x % 2);
```

### RefCountTable
Reference-counted resource manager for creating message hubs and preventing memory leaks. Resources are automatically cleaned up when all references are disposed.

```csharp
var hub = RefCountTable.Create<string, ISubject<string>>(...);
var ref1 = await hub.GetOrCreateAsync("topic", ct);
// Automatic cleanup when last reference is disposed
```

- Idempotent - consumers/producers can appear in any order
- Thread-safe concurrent access
- Perfect for dynamic message hubs and resource pooling
