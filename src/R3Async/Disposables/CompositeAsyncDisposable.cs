using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public sealed class CompositeAsyncDisposable : IAsyncDisposable
{
    List<IAsyncDisposable?> list; // when removed, set null
    readonly object gate = new object();
    bool isDisposed;
    int count;

    const int ShrinkThreshold = 64;

    public bool IsDisposed => Volatile.Read(ref isDisposed);

    public CompositeAsyncDisposable()
    {
        this.list = new();
    }

    public CompositeAsyncDisposable(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.list = new(capacity);
    }

    public CompositeAsyncDisposable(params IAsyncDisposable[] disposables)
    {
        this.list = new(disposables);
        this.count = list.Count;
    }

    public CompositeAsyncDisposable(IEnumerable<IAsyncDisposable> disposables)
    {
        this.list = new(disposables);
        this.count = list.Count;
    }

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

    public bool IsReadOnly => false;

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

    public bool Contains(IAsyncDisposable item)
    {
        lock (gate)
        {
            if (isDisposed) return false;
            return list.Contains(item);
        }
    }

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