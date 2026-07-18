// Inspired by AsyncGate from dotnet/reactive:
// https://github.com/dotnet/reactive/blob/main/AsyncRx.NET/System.Reactive.Async/Threading/AsyncGate.cs
//
// One difference: reentrancy is validated with a LockOwner token instead of a bare AsyncLocal count.
// A flow is treated as reentrant only while its token is the gate's current holder, so a flow forked
// while the gate was held stops bypassing the gate
// as soon as the holder releases it.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Internals
{
    internal sealed class AsyncGate
    {
        private readonly object _gate = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly AsyncLocal<LockOwner?> _owner = new();
        private LockOwner? _currentOwner;

        [DebuggerStepThrough]
        public ValueTask<Releaser> LockAsync()
        {
            LockOwner newOwner;

            lock (_gate)
            {
                var owner = _owner.Value;
               
                if (owner is not null && ReferenceEquals(owner, _currentOwner))
                {
                    owner.RecursionCount++;
                    return new ValueTask<Releaser>(new Releaser(this, owner));
                }

                newOwner = new LockOwner();
                _owner.Value = newOwner;
            }

            return new ValueTask<Releaser>(_semaphore.WaitAsync().ContinueWith(_ =>
            {
                lock (_gate)
                {
                    _currentOwner = newOwner;
                }

                return new Releaser(this, newOwner);
            }));
        }

        private void Release(LockOwner owner)
        {
            lock (_gate)
            {
                Debug.Assert(owner.RecursionCount > 0);

                if (--owner.RecursionCount == 0)
                {
                    _currentOwner = null;
                    _semaphore.Release();
                }
            }
        }

        internal sealed class LockOwner
        {
            public int RecursionCount = 1;
        }

        public readonly struct Releaser : IDisposable
        {
            private readonly AsyncGate _parent;
            private readonly LockOwner _owner;

            internal Releaser(AsyncGate parent, LockOwner owner)
            {
                _parent = parent;
                _owner = owner;
            }

            public void Dispose() => _parent.Release(_owner);
        }
    }
}
