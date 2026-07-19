using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

/// <summary>
/// Aggregates multiple <see cref="IAsyncDisposable"/> instances so they can be disposed together. Disposables can
/// be added and removed while the composite is active; once the composite itself is disposed, all contained
/// disposables are disposed and any subsequently added disposable is disposed immediately instead of being stored.
/// </summary>
public sealed class CompositeAsyncDisposable : IAsyncDisposable
{
    List<IAsyncDisposable?> list; // when removed, set null
    readonly object gate = new object();
    bool isDisposed;
    int count;

    const int ShrinkThreshold = 64;

    /// <summary>Gets whether this composite has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref isDisposed);

    /// <summary>Creates an empty <see cref="CompositeAsyncDisposable"/>.</summary>
    public CompositeAsyncDisposable()
    {
        this.list = new();
    }

    /// <summary>Creates an empty <see cref="CompositeAsyncDisposable"/> with an initial internal capacity hint.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public CompositeAsyncDisposable(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.list = new(capacity);
    }

    /// <summary>Creates a <see cref="CompositeAsyncDisposable"/> containing the given disposables.</summary>
    public CompositeAsyncDisposable(params IAsyncDisposable[] disposables)
    {
        this.list = new(disposables);
        this.count = list.Count;
    }

    /// <summary>Creates a <see cref="CompositeAsyncDisposable"/> containing the given disposables.</summary>
    public CompositeAsyncDisposable(IEnumerable<IAsyncDisposable> disposables)
    {
        this.list = new(disposables);
        this.count = list.Count;
    }

    /// <summary>Gets the number of disposables currently contained in this composite.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }

    /// <summary>Always <see langword="false"/>; retained for collection-like API compatibility.</summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Adds <paramref name="item"/> to the composite. If the composite has already been disposed,
    /// <paramref name="item"/> is disposed immediately instead of being stored.
    /// </summary>
    public ValueTask AddAsync(IAsyncDisposable item)
    {
        lock (gate)
        {
            if (!isDisposed)
            {
                count += 1;
                list.Add(item);
                return default;
            }
        }

        // CompositeDisposable is Disposed.
        return item.DisposeAsync();
    }

    /// <summary>
    /// Removes <paramref name="item"/> from the composite and disposes it. Returns <see langword="true"/> if the
    /// item was found and removed; returns <see langword="false"/> without disposing it if the composite is
    /// already disposed or the item is not present.
    /// </summary>
    public async ValueTask<bool> Remove(IAsyncDisposable item)
    {
        lock (gate)
        {
            // CompositeDisposable is Disposed, do nothing.
            if (isDisposed) return false;

            var current = list;

            var index = current.IndexOf(item);
            if (index == -1)
            {
                // not found
                return false;
            }

            // don't do RemoveAt(avoid Array Copy)
            current[index] = null;

            // Do shrink
            if (current.Capacity > ShrinkThreshold && count < current.Capacity / 2)
            {
                var fresh = new List<IAsyncDisposable?>(current.Capacity / 2);

                foreach (var d in current)
                {
                    if (d != null)
                    {
                        fresh.Add(d);
                    }
                }

                list = fresh;
            }

            count -= 1;
        }

        // Dispose outside of lock
        await item.DisposeAsync();
        return true;
    }

    /// <summary>
    /// Disposes all currently contained disposables and removes them from the composite, without disposing the
    /// composite itself (it can still accept new disposables afterward). Does nothing if already disposed or empty.
    /// </summary>
    public async ValueTask Clear()
    {
        IAsyncDisposable?[] targetDisposables;
        int clearCount;
        lock (gate)
        {
            // CompositeDisposable is Disposed, do nothing.
            if (isDisposed) return;
            if (count == 0) return;

            targetDisposables = ArrayPool<IAsyncDisposable?>.Shared.Rent(list.Count);
            clearCount = list.Count;

            list.CopyTo(targetDisposables);

            list.Clear();
            count = 0;
        }

        // Dispose outside of lock
        try
        {
            foreach (var item in targetDisposables.Take(clearCount))
            {
                if (item != null)
                {
                    await item.DisposeAsync();
                }

            }
        }
        finally
        {
            ArrayPool<IAsyncDisposable?>.Shared.Return(targetDisposables, clearArray: true);
        }
    }

    /// <summary>Gets whether <paramref name="item"/> is currently contained in the composite.</summary>
    public bool Contains(IAsyncDisposable item)
    {
        lock (gate)
        {
            if (isDisposed) return false;
            return list.Contains(item);
        }
    }

    /// <summary>Copies the contained disposables to <paramref name="array"/>, starting at <paramref name="arrayIndex"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is out of range, or the array is too small to hold all elements.</exception>
    public void CopyTo(IAsyncDisposable[] array, int arrayIndex)
    {
        if (arrayIndex < 0 || arrayIndex >= array.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        lock (gate)
        {
            if (isDisposed) return;

            if (arrayIndex + count > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }

            var i = 0;
            foreach (var item in list)
            {
                if (item != null)
                {
                    array[arrayIndex + i++] = item;
                }
            }
        }
    }

    /// <summary>
    /// Disposes the composite and all currently contained disposables. Safe to call multiple times; subsequent
    /// calls are no-ops. After this call, any disposable passed to <see cref="AddAsync"/> is disposed immediately.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        List<IAsyncDisposable?> disposables;

        lock (gate)
        {
            if (isDisposed) return;

            count = 0;
            isDisposed = true;
            disposables = list;
            list = null!; // dereference.
        }

        foreach (var item in disposables)
        {
            if (item is not null)
            {
                await item.DisposeAsync();

            }
        }
        disposables.Clear();
    }

    /// <summary>
    /// Returns an enumerator that atomically snapshots and clears the contained disposables as it enumerates them;
    /// the composite itself is left empty (but not disposed) once enumeration completes. This does not dispose the
    /// yielded items - the caller owns them after enumeration.
    /// </summary>
    public IEnumerator<IAsyncDisposable> GetEnumerator()
    {
        lock (gate)
        {
            // make snapshot
            return EnumerateAndClear(list.ToArray()).GetEnumerator();
        }
    }

    static IEnumerable<IAsyncDisposable> EnumerateAndClear(IAsyncDisposable?[] disposables)
    {
        try
        {
            foreach (var item in disposables)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
        finally
        {
            disposables.AsSpan().Clear();
        }
    }
}