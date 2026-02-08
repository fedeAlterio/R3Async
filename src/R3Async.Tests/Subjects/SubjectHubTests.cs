using R3Async.Subjects;
using Shouldly;

namespace R3Async.Tests.Subjects;

public class ConnectionHubTest
{
    [Fact]
    public async Task SubjectHub_GetOrCreateConnection_CreatesNewSubjectForNewKey()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        await using var connection = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        connection.Subject.ShouldNotBeNull();
    }

    [Fact]
    public async Task SubjectHub_GetOrCreateConnection_ReturnsSameSubjectForSameKey()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        await using var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        connection1.Subject.ShouldBe(connection2.Subject);
    }

    [Fact]
    public async Task SubjectHub_GetOrCreateConnection_CreatesDifferentSubjectsForDifferentKeys()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        await using var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateConnectionAsync("key2", CancellationToken.None);

        connection1.Subject.ShouldNotBe(connection2.Subject);
    }

    [Fact]
    public async Task SubjectHub_DisposeConnection_RemovesSubjectWhenAllConnectionsDisposed()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        var subject1 = connection1.Subject;
        var connection2 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        await connection1.DisposeAsync();
        await connection2.DisposeAsync();

        // Get a new connection - should create a new subject
        await using var connection3 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        connection3.Subject.ShouldNotBe(subject1);
    }

    [Fact]
    public async Task SubjectHub_MultipleConnections_SharesSameSubject()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        await using var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        var results1 = new List<int>();
        var results2 = new List<int>();
        var results3 = new List<int>();

        await using var sub1 = await connection1.Subject.Values.SubscribeAsync(
            async (x, token) => results1.Add(x),
            CancellationToken.None);

        await using var sub2 = await connection2.Subject.Values.SubscribeAsync(
            async (x, token) => results2.Add(x),
            CancellationToken.None);

        await using var sub3 = await connection3.Subject.Values.SubscribeAsync(
            async (x, token) => results3.Add(x),
            CancellationToken.None);

        await connection1.Subject.OnNextAsync(42, CancellationToken.None);

        results1.ShouldBe(new[] { 42 });
        results2.ShouldBe(new[] { 42 });
        results3.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task SubjectHub_RefCounting_KeepsSubjectAliveWhileConnectionsExist()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        var subject = connection1.Subject;
        var connection2 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        // Dispose first connection
        await connection1.DisposeAsync();

        // Second connection should still have the same subject
        var connection3 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        connection3.Subject.ShouldBe(subject);

        await connection2.DisposeAsync();
        await connection3.DisposeAsync();
    }

    [Fact]
    public async Task SubjectHub_FactoryWithKey_PassesKeyToFactory()
    {
        var capturedKeys = new List<string>();
        var hub = ConnectionHub.Create<string, int>(key =>
        {
            capturedKeys.Add(key);
            return Subject.Create<int>();
        });

        await using var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateConnectionAsync("key2", CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        capturedKeys.ShouldBe(new[] { "key1", "key2" });
    }

    [Fact]
    public async Task SubjectHub_DisposeConnection_MultipleTimes_DoesNotThrow()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        var connection = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);

        await connection.DisposeAsync();
        await connection.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SubjectHub_AccessDisposedConnection_ThrowsObjectDisposedException()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());

        var connection = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await connection.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => connection.Subject);
    }

    [Fact]
    public async Task SubjectHub_FactoryThrows_ExceptionPropagates()
    {
        var hub = ConnectionHub.Create<string, int>(key => throw new InvalidOperationException("Factory error"));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None));
    }

    [Fact]
    public async Task SubjectHub_FactoryThrows_SubjectNotCached()
    {
        var callCount = 0;
        var hub = ConnectionHub.Create<string, int>(key =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("First call fails");
            return Subject.Create<int>();
        });

        // First call should throw
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None));

        // Second call should succeed and create a new subject
        await using var connection = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        connection.Subject.ShouldNotBeNull();
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task SubjectHub_CancellationToken_ThrowsOperationCanceledException()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await hub.GetOrCreateConnectionAsync("key1", cts.Token));
    }

    [Fact]
    public async Task SubjectHub_BehaviorSubject_LateSubscriberReceivesLastValue()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.CreateBehavior(0));

        await using var connection1 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        await connection1.Subject.OnNextAsync(42, CancellationToken.None);

        await using var connection2 = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
        var results = new List<int>();
        await using var sub = await connection2.Subject.Values.SubscribeAsync(
            async (x, token) => results.Add(x),
            CancellationToken.None);

        results.ShouldBe(new[] { 42 });
    }

    [Fact]
    public async Task SubjectHub_IntKeys_WorksCorrectly()
    {
        var hub = ConnectionHub.Create<int, string>(key => Subject.Create<string>());

        await using var connection1 = await hub.GetOrCreateConnectionAsync(1, CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateConnectionAsync(2, CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateConnectionAsync(1, CancellationToken.None);

        connection1.Subject.ShouldBe(connection3.Subject);
        connection1.Subject.ShouldNotBe(connection2.Subject);
    }

    [Fact]
    public async Task SubjectHub_ComplexKey_WorksCorrectly()
    {
        var hub = ConnectionHub.Create<(string, int), int>(key => Subject.Create<int>());

        await using var connection1 = await hub.GetOrCreateConnectionAsync(("a", 1), CancellationToken.None);
        await using var connection2 = await hub.GetOrCreateConnectionAsync(("a", 2), CancellationToken.None);
        await using var connection3 = await hub.GetOrCreateConnectionAsync(("a", 1), CancellationToken.None);

        connection1.Subject.ShouldBe(connection3.Subject);
        connection1.Subject.ShouldNotBe(connection2.Subject);
    }

    [Fact]
    public async Task SubjectHub_ConcurrentAccess_AllConnectionsShareSameSubject()
    {
        var hub = ConnectionHub.Create<string, int>(key => Subject.Create<int>());
        var connections = new List<IConnection<int>>();

        // Create 10 connections concurrently
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var connection = await hub.GetOrCreateConnectionAsync("key1", CancellationToken.None);
            lock (connections)
            {
                connections.Add(connection);
            }
        });

        await Task.WhenAll(tasks);

        // All connections should have the same subject
        var firstSubject = connections[0].Subject;
        connections.All(c => c.Subject == firstSubject).ShouldBeTrue();

        // Cleanup
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task SubjectHub_NullFactory_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            ConnectionHub.Create<string, int>(null!));
    }
}
