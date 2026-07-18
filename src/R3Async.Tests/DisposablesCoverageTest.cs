using R3Async.Helpers;
using Shouldly;

namespace R3Async.Tests;

public class DisposablesCoverageTest
{
    static IAsyncDisposable Tracked(Action onDispose) => AsyncDisposable.Create(onDispose);

    [Fact]
    public async Task CompositeAsyncDisposable_AddRemoveDispose()
    {
        var disposed = new List<int>();
        var composite = new CompositeAsyncDisposable();

        var d1 = Tracked(() => disposed.Add(1));
        var d2 = Tracked(() => disposed.Add(2));

        await composite.AddAsync(d1);
        await composite.AddAsync(d2);

        composite.Count.ShouldBe(2);
        composite.IsReadOnly.ShouldBeFalse();
        composite.Contains(d1).ShouldBeTrue();

        (await composite.Remove(d1)).ShouldBeTrue();
        disposed.ShouldBe(new[] { 1 });
        (await composite.Remove(d1)).ShouldBeFalse();

        await composite.DisposeAsync();
        composite.IsDisposed.ShouldBeTrue();
        disposed.ShouldBe(new[] { 1, 2 });

        await composite.DisposeAsync();
        composite.Contains(d2).ShouldBeFalse();
        (await composite.Remove(d2)).ShouldBeFalse();
    }

    [Fact]
    public async Task CompositeAsyncDisposable_AddAfterDispose_DisposesItem()
    {
        var composite = new CompositeAsyncDisposable();
        await composite.DisposeAsync();

        var disposed = false;
        await composite.AddAsync(Tracked(() => disposed = true));

        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task CompositeAsyncDisposable_Clear_DisposesAllButStaysUsable()
    {
        var disposedCount = 0;
        var composite = new CompositeAsyncDisposable(2);

        await composite.AddAsync(Tracked(() => disposedCount++));
        await composite.AddAsync(Tracked(() => disposedCount++));

        await composite.Clear();
        disposedCount.ShouldBe(2);
        composite.Count.ShouldBe(0);
        composite.IsDisposed.ShouldBeFalse();

        await composite.Clear();
        disposedCount.ShouldBe(2);
    }

    [Fact]
    public async Task CompositeAsyncDisposable_Constructors()
    {
        var d1 = Tracked(() => { });
        var d2 = Tracked(() => { });

        var fromParams = new CompositeAsyncDisposable(d1, d2);
        fromParams.Count.ShouldBe(2);
        await fromParams.DisposeAsync();

        var fromEnumerable = new CompositeAsyncDisposable(new List<IAsyncDisposable> { Tracked(() => { }) });
        fromEnumerable.Count.ShouldBe(1);
        await fromEnumerable.DisposeAsync();

        Should.Throw<ArgumentOutOfRangeException>(() => new CompositeAsyncDisposable(-1));
    }

    [Fact]
    public async Task CompositeAsyncDisposable_CopyToAndEnumerate()
    {
        var d1 = Tracked(() => { });
        var d2 = Tracked(() => { });
        var composite = new CompositeAsyncDisposable(d1, d2);

        var array = new IAsyncDisposable[3];
        composite.CopyTo(array, 1);
        array[1].ShouldBe(d1);
        array[2].ShouldBe(d2);

        Should.Throw<ArgumentOutOfRangeException>(() => composite.CopyTo(new IAsyncDisposable[3], -1));
        Should.Throw<ArgumentOutOfRangeException>(() => composite.CopyTo(new IAsyncDisposable[2], 1));

        var enumerated = new List<IAsyncDisposable>();
        using (var enumerator = composite.GetEnumerator())
        {
            while (enumerator.MoveNext())
            {
                enumerated.Add(enumerator.Current);
            }
        }

        enumerated.ShouldBe(new[] { d1, d2 });

        await composite.DisposeAsync();
    }

    [Fact]
    public async Task CompositeAsyncDisposable_RemoveManyItems_Shrinks()
    {
        var composite = new CompositeAsyncDisposable(200);
        var items = Enumerable.Range(0, 100).Select(_ => Tracked(() => { })).ToArray();

        foreach (var item in items)
        {
            await composite.AddAsync(item);
        }

        foreach (var item in items.Take(80))
        {
            (await composite.Remove(item)).ShouldBeTrue();
        }

        composite.Count.ShouldBe(20);
        await composite.DisposeAsync();
    }

    [Fact]
    public async Task SingleAssignmentAsyncDisposable_Lifecycle()
    {
        var single = new SingleAssignmentAsyncDisposable();
        single.IsDisposed.ShouldBeFalse();
        single.GetDisposable().ShouldBeNull();

        var disposed = false;
        var inner = Tracked(() => disposed = true);
        await single.SetDisposableAsync(inner);
        single.GetDisposable().ShouldBe(inner);

        await Should.ThrowAsync<InvalidOperationException>(async () => await single.SetDisposableAsync(Tracked(() => { })));

        await single.DisposeAsync();
        disposed.ShouldBeTrue();
        single.IsDisposed.ShouldBeTrue();
        single.GetDisposable().ShouldBe(AsyncDisposable.Empty);

        var lateDisposed = false;
        await single.SetDisposableAsync(Tracked(() => lateDisposed = true));
        lateDisposed.ShouldBeTrue();

        await single.SetDisposableAsync(null);
        await single.DisposeAsync();
    }

    [Fact]
    public async Task SerialAsyncDisposable_Lifecycle()
    {
        var serial = new SerialAsyncDisposable();
        await serial.SetDisposableAsync(null);

        var firstDisposed = false;
        await serial.SetDisposableAsync(Tracked(() => firstDisposed = true));

        var secondDisposed = false;
        await serial.SetDisposableAsync(Tracked(() => secondDisposed = true));
        firstDisposed.ShouldBeTrue();
        secondDisposed.ShouldBeFalse();

        await serial.DisposeAsync();
        secondDisposed.ShouldBeTrue();

        var lateDisposed = false;
        await serial.SetDisposableAsync(Tracked(() => lateDisposed = true));
        lateDisposed.ShouldBeTrue();

        await serial.SetDisposableAsync(null);
        await serial.DisposeAsync();
    }

    [Fact]
    public async Task AsyncDisposable_CreateOverloads_DisposeOnce()
    {
        var syncCount = 0;
        var sync = AsyncDisposable.Create(() => syncCount++);
        await sync.DisposeAsync();
        await sync.DisposeAsync();
        syncCount.ShouldBe(1);

        var asyncCount = 0;
        var async = AsyncDisposable.Create(() =>
        {
            asyncCount++;
            return default(ValueTask);
        });
        await async.DisposeAsync();
        await async.DisposeAsync();
        asyncCount.ShouldBe(1);

        await AsyncDisposable.Empty.DisposeAsync();
    }

    [Fact]
    public async Task AsyncDisposableEx_ToAsyncDisposable_DisposesUnderlying()
    {
        var disposed = false;
        var disposable = new TestDisposable(() => disposed = true);

        await disposable.ToAsyncDisposable().DisposeAsync();
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncDisposableValue_DelegatesToDisposable()
    {
        var disposed = false;
        var value = new AsyncDisposableValue<int>
        {
            Value = 42,
            Disposable = Tracked(() => disposed = true)
        };

        value.Value.ShouldBe(42);
        await value.DisposeAsync();
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RefCountLazy_SharesValueAndResets()
    {
        var created = 0;
        var disposedCount = 0;
        var lazy = new RefCountLazy<int>(async ct =>
        {
            created++;
            return new AsyncDisposableValue<int>
            {
                Value = 42,
                Disposable = Tracked(() => disposedCount++)
            };
        });

        var r1 = await lazy.GetAsync(CancellationToken.None);
        var r2 = await lazy.GetAsync(CancellationToken.None);
        created.ShouldBe(1);
        r1.Value.ShouldBe(42);
        r2.Value.ShouldBe(42);

        await r1.DisposeAsync();
        await r1.DisposeAsync();
        disposedCount.ShouldBe(0);

        await r2.DisposeAsync();
        disposedCount.ShouldBe(1);
        Should.Throw<ObjectDisposedException>(() => r2.Value);

        var r3 = await lazy.GetAsync(CancellationToken.None);
        created.ShouldBe(2);
        await r3.DisposeAsync();
        disposedCount.ShouldBe(2);
    }

    [Fact]
    public async Task RefCountLazy_FactoryThrows_DoesNotLeakRefCount()
    {
        var calls = 0;
        var disposedCount = 0;
        var lazy = new RefCountLazy<int>(async ct =>
        {
            if (calls++ == 0)
                throw new InvalidOperationException("factory failed");

            return new AsyncDisposableValue<int>
            {
                Value = 42,
                Disposable = AsyncDisposable.Create(() => disposedCount++)
            };
        });

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await lazy.GetAsync(CancellationToken.None));

        // The failed connection must be discarded: the next caller gets a fresh one,
        // and the refcount is not off by one, so the value is disposed at zero.
        var reference = await lazy.GetAsync(CancellationToken.None);
        reference.Value.ShouldBe(42);
        await reference.DisposeAsync();
        disposedCount.ShouldBe(1);
    }

    sealed class TestDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
