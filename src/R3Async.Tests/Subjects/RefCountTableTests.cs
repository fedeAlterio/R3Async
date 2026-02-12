using R3Async;
using R3Async.Subjects;
using Shouldly;

namespace R3Async.Tests.Subjects;

public class RefCountTableTests
{
    [Fact]
    public async Task ConnectionHub_GetOrCreateConnection_CreatesNewSubjectForNewKey()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        connection.Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task ConnectionHub_GetOrCreateConnection_ReturnsSameSubjectForSameKey()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        connection1.Value.ShouldBe(connection2.Value);
    }

    [Fact]
    public async Task ConnectionHub_GetOrCreateConnection_CreatesDifferentSubjectsForDifferentKeys()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateAsync("key2", CancellationToken.None);

        connection1.Value.ShouldNotBe(connection2.Value);
    }

    [Fact]
    public async Task ConnectionHub_DisposeConnection_RemovesSubjectWhenAllConnectionsDisposed()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        var subject1 = connection1.Value;
        var connection2 = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        await connection1.DisposeAsync();
        await connection2.DisposeAsync();

        // Get a new connection - should create a new subject
        await using var connection3 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        connection3.Value.ShouldNotBe(subject1);
    }

    [Fact]
    public async Task ConnectionHub_MultipleConnections_SharesSameSubject()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        var results1 = new List<int>();
        var results2 = new List<int>();
        var results3 = new List<int>();

        await using var sub1 = await connection1.Value.Values.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await using var sub2 = await connection2.Value.Values.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await using var sub3 = await connection3.Value.Values.SubscribeAsync(
            async (x, token) => results3.Add(x),
            CancellationToken.None);

        await connection1.Value.OnNextAsync(42, CancellationToken.None);

        results1.ShouldBe(new[] { 42 });
        results2.ShouldBe(new[] { 42 });
        results3.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task ConnectionHub_RefCounting_KeepsSubjectAliveWhileConnectionsExist()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        var subject = connection1.Value;
        var connection2 = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        // Dispose first connection
        await connection1.DisposeAsync();

        // Second connection should still have the same subject
        var connection3 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        connection3.Value.ShouldBe(subject);

        await connection2.DisposeAsync();
        await connection3.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionHub_FactoryWithKey_PassesKeyToFactory()
    {
        var capturedKeys = new List<string>();
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) =>
        {
            capturedKeys.Add(key);
            return Task.FromResult(new RefCountTable.Entry<ISubject<int>>
            {
                Value = Subject.Create<int>(),
                Disposable = AsyncDisposable.Empty
            });
        });

        await using var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateAsync("key2", CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        capturedKeys.ShouldBe(new[] { "key1", "key2" });
    }

    [Fact]
    public async Task ConnectionHub_DisposeConnection_MultipleTimes_DoesNotThrow()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        var connection = await hub.GetOrCreateAsync("key1", CancellationToken.None);

        await connection.DisposeAsync();
        await connection.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionHub_AccessDisposedConnection_ThrowsObjectDisposedException()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        var connection = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await connection.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => connection.Value);
    }

    [Fact]
    public async Task ConnectionHub_FactoryThrows_ExceptionPropagates()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => throw new InvalidOperationException("Factory error"));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await hub.GetOrCreateAsync("key1", CancellationToken.None));
    }

    [Fact]
    public async Task ConnectionHub_FactoryThrows_SubjectNotCached()
    {
        var callCount = 0;
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("First call fails");
            return Task.FromResult(new RefCountTable.Entry<ISubject<int>>
            {
                Value = Subject.Create<int>(),
                Disposable = AsyncDisposable.Empty
            });
        });

        // First call should throw
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await hub.GetOrCreateAsync("key1", CancellationToken.None));

        // Second call should succeed and create a new subject
        await using var connection = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        connection.Value.ShouldNotBeNull();
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task ConnectionHub_CancellationToken_ThrowsOperationCanceledException()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await hub.GetOrCreateAsync("key1", cts.Token));
    }

    [Fact]
    public async Task ConnectionHub_BehaviorSubject_LateSubscriberReceivesLastValue()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.CreateBehavior(0),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection1 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        await connection1.Value.OnNextAsync(42, CancellationToken.None);

        await using var connection2 = await hub.GetOrCreateAsync("key1", CancellationToken.None);
        var results = new List<int>();
        await using var sub = await connection2.Value.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task ConnectionHub_IntKeys_WorksCorrectly()
    {
        var hub = new RefCountTable<int, ISubject<string>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<string>>
        {
            Value = Subject.Create<string>(),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection1 = await hub.GetOrCreateAsync(1, CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateAsync(2, CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateAsync(1, CancellationToken.None);

        connection1.Value.ShouldBe(connection3.Value);
        connection1.Value.ShouldNotBe(connection2.Value);
    }

    [Fact]
    public async Task ConnectionHub_ComplexKey_WorksCorrectly()
    {
        var hub = new RefCountTable<(string, int), ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));

        await using var connection1 = await hub.GetOrCreateAsync(("a", 1), CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateAsync(("a", 2), CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateAsync(("a", 1), CancellationToken.None);

        connection1.Value.ShouldBe(connection3.Value);
        connection1.Value.ShouldNotBe(connection2.Value);
    }

    [Fact]
    public async Task ConnectionHub_ConcurrentAccess_AllConnectionsShareSameSubject()
    {
        var hub = new RefCountTable<string, ISubject<int>>((key, ct) => Task.FromResult(new RefCountTable.Entry<ISubject<int>>
        {
            Value = Subject.Create<int>(),
            Disposable = AsyncDisposable.Empty
        }));
        var connections = new List<RefCountTable<string, ISubject<int>>.Reference>();

        // Create 10 connections concurrently
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var connection = await hub.GetOrCreateAsync("key1", CancellationToken.None);
            lock (connections)
            {
                connections.Add(connection);
            }
        });

        await Task.WhenAll(tasks);

        // All connections should have the same subject
        var firstSubject = connections[0].Value;
        connections.All(c => c.Value == firstSubject).ShouldBeTrue();

        // Cleanup
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public void ConnectionHub_NullFactory_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new RefCountTable<string, ISubject<int>>(null!));
    }
}
