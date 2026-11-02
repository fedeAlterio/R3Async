// Inspired by AsyncGate from dotnet/reactive:
// https://github.com/dotnet/reactive/blob/main/AsyncRx.NET/System.Reactive.Async/Threading/AsyncGate.cs
//
// Reentrancy is tracked with a LockOwner token in ExecutionFlow. Waiters are queued FIFO and resumed inline
// on the releasing thread, so never dispose a Releaser while holding another lock.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace R3Async.Internals
{
    internal sealed class AsyncGate
    {
        private const int MaxNestedInlineHandoffs = 16;

        [ThreadStatic]
        private static int t_nestedInlineHandoffs;

        private readonly object _gate = new();
        private LockOwner? _currentOwner;
        private Waiter? _head;
        private Waiter? _tail;
        private Waiter? _pool;

        [DebuggerStepThrough]
        public ValueTask<Releaser> LockAsync(CancellationToken cancellationToken)
        {
            Waiter waiter;

            lock (_gate)
            {
                if (TryReenter(out var releaser))
                {
                    return new ValueTask<Releaser>(releaser);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<Releaser>(Task.FromCanceled<Releaser>(cancellationToken));
                }

                if (TryAcquireFree(out releaser))
                {
                    return new ValueTask<Releaser>(releaser);
                }

                var newOwner = new LockOwner();
                ExecutionFlow.Enter(newOwner);
                waiter = _pool ?? new Waiter(this);
                _pool = waiter.Next;
                waiter.Prepare(newOwner, cancellationToken);
                Append(waiter);
            }

            if (cancellationToken.CanBeCanceled)
            {
                waiter.Registration = cancellationToken.Register(static state => ((Waiter)state!).Cancel(), waiter);
            }

            return new ValueTask<Releaser>(waiter, waiter.Version);
        }

        [DebuggerStepThrough]
        public ValueTask<Releaser> LockAsync() => LockAsync(CancellationToken.None);

        public bool TryLock(out Releaser releaser)
        {
            lock (_gate)
            {
                return TryReenter(out releaser) || TryAcquireFree(out releaser);
            }
        }

        private bool TryReenter(out Releaser releaser)
        {
            var owner = _currentOwner;
            if (owner is not null && ExecutionFlow.Contains(owner))
            {
                owner.Count++;
                releaser = new Releaser(this, owner);
                return true;
            }

            releaser = default;
            return false;
        }

        private bool TryAcquireFree(out Releaser releaser)
        {
            if (_currentOwner is not null)
            {
                releaser = default;
                return false;
            }

            var newOwner = new LockOwner();
            ExecutionFlow.Enter(newOwner);
            _currentOwner = newOwner;
            releaser = new Releaser(this, newOwner);
            return true;
        }

        private void Release(LockOwner owner)
        {
            Waiter? next;
            lock (_gate)
            {
                Debug.Assert(owner.Count > 0);

                if (--owner.Count != 0)
                {
                    return;
                }

                next = _head;
                if (next is not null)
                {
                    Unlink(next);
                }

                _currentOwner = next?.Owner;
            }

            next?.Grant();
        }

        private void Append(Waiter waiter)
        {
            waiter.Previous = _tail;
            waiter.Next = null;
            if (_tail is null)
            {
                _head = waiter;
            }
            else
            {
                _tail.Next = waiter;
            }

            _tail = waiter;
            waiter.Queued = true;
        }

        private void Unlink(Waiter waiter)
        {
            if (waiter.Previous is null)
            {
                _head = waiter.Next;
            }
            else
            {
                waiter.Previous.Next = waiter.Next;
            }

            if (waiter.Next is null)
            {
                _tail = waiter.Previous;
            }
            else
            {
                waiter.Next.Previous = waiter.Previous;
            }

            waiter.Previous = null;
            waiter.Next = null;
            waiter.Queued = false;
        }

        internal sealed class LockOwner : FlowToken;

        private sealed class Waiter(AsyncGate gate) : IValueTaskSource<Releaser>
        {
            private ManualResetValueTaskSourceCore<Releaser> _core;
            private CancellationToken _cancellationToken;

            public LockOwner? Owner;
            public Waiter? Previous;
            public Waiter? Next;
            public bool Queued;
            public CancellationTokenRegistration Registration;

            public short Version => _core.Version;

            public void Prepare(LockOwner owner, CancellationToken cancellationToken)
            {
                Owner = owner;
                _cancellationToken = cancellationToken;
            }

            public void Grant()
            {
                var releaser = new Releaser(gate, Owner!);
                if (t_nestedInlineHandoffs >= MaxNestedInlineHandoffs)
                {
                    _core.RunContinuationsAsynchronously = true;
                    _core.SetResult(releaser);
                    return;
                }

                _core.RunContinuationsAsynchronously = false;
                t_nestedInlineHandoffs++;
                try
                {
                    _core.SetResult(releaser);
                }
                finally
                {
                    t_nestedInlineHandoffs--;
                }
            }

            public void Cancel()
            {
                lock (gate._gate)
                {
                    if (!Queued)
                    {
                        return;
                    }

                    gate.Unlink(this);
                }

                // The owner never held the gate: marking it finished lets ExecutionFlow drop its frame.
                Volatile.Write(ref Owner!.Count, 0);
                _core.RunContinuationsAsynchronously = true;
                _core.SetException(new OperationCanceledException(_cancellationToken));
            }

            public Releaser GetResult(short token)
            {
                try
                {
                    return _core.GetResult(token);
                }
                finally
                {
                    Registration.Dispose();
                    Registration = default;
                    Owner = null;
                    _cancellationToken = default;
                    _core.Reset();
                    lock (gate._gate)
                    {
                        Next = gate._pool;
                        gate._pool = this;
                    }
                }
            }

            public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
                => _core.OnCompleted(continuation, state, token, flags);
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
