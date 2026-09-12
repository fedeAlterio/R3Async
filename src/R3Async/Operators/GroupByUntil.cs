using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;
using R3Async.Subjects;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Partitions <paramref name="source"/> into groups keyed by <paramref name="keySelector"/>, closing each group
    /// when the task returned by <paramref name="durationSelector"/> completes. A later value with the same key
    /// starts a brand new group.
    /// </summary>
    /// <remarks>
    /// Unlike <c>GroupBy</c>, which keeps one subject alive per distinct key for the lifetime of the subscription,
    /// this operator releases a group once its duration elapses. Use it when keys are unbounded or long-lived
    /// (per-device, per-session, per-variable streams) so that idle keys do not accumulate.
    /// <para>
    /// The duration is a <see cref="Task"/> rather than an observable: an observable duration would signal from
    /// inside its own operator's lock, inverting lock order against this one. The <see cref="CancellationToken"/>
    /// passed to the selector is cancelled when the group is torn down.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TValue">The type of the source and group values.</typeparam>
    /// <param name="source">The observable to partition.</param>
    /// <param name="keySelector">Computes the group key for each source value.</param>
    /// <param name="durationSelector">Given a group and a teardown token, returns the task that closes it.</param>
    public static AsyncObservable<GroupedAsyncObservable<TKey, TValue>> GroupByUntil<TKey, TValue>(this AsyncObservable<TValue> source,
                                                                                                   Func<TValue, TKey> keySelector,
                                                                                                   Func<GroupedAsyncObservable<TKey, TValue>, CancellationToken, Task> durationSelector)
        where TKey : notnull
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        if (durationSelector == null) throw new ArgumentNullException(nameof(durationSelector));
        return new GroupByUntilAsyncObservable<TKey, TValue>(source, keySelector, durationSelector, static _ => Subject.Create<TValue>());
    }

    /// <summary>
    /// Partitions <paramref name="source"/> into groups keyed by <paramref name="keySelector"/>, closing each group
    /// when the task returned by <paramref name="durationSelector"/> completes, and using
    /// <paramref name="groupSubjectSelector"/> to create the subject backing each group.
    /// </summary>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TValue">The type of the source and group values.</typeparam>
    /// <param name="source">The observable to partition.</param>
    /// <param name="keySelector">Computes the group key for each source value.</param>
    /// <param name="durationSelector">Given a group and a teardown token, returns the task that closes it.</param>
    /// <param name="groupSubjectSelector">Creates the subject used to back each group, given its key.</param>
    public static AsyncObservable<GroupedAsyncObservable<TKey, TValue>> GroupByUntil<TKey, TValue>(this AsyncObservable<TValue> source,
                                                                                                   Func<TValue, TKey> keySelector,
                                                                                                   Func<GroupedAsyncObservable<TKey, TValue>, CancellationToken, Task> durationSelector,
                                                                                                   Func<TKey, ISubject<TValue>> groupSubjectSelector)
        where TKey : notnull
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        if (durationSelector == null) throw new ArgumentNullException(nameof(durationSelector));
        if (groupSubjectSelector == null) throw new ArgumentNullException(nameof(groupSubjectSelector));
        return new GroupByUntilAsyncObservable<TKey, TValue>(source, keySelector, durationSelector, groupSubjectSelector);
    }

    sealed class GroupByUntilAsyncObservable<TKey, TValue>(AsyncObservable<TValue> source,
                                                           Func<TValue, TKey> keySelector,
                                                           Func<GroupedAsyncObservable<TKey, TValue>, CancellationToken, Task> durationSelector,
                                                           Func<TKey, ISubject<TValue>> groupSubjectSelector) : AsyncObservable<GroupedAsyncObservable<TKey, TValue>>
        where TKey : notnull
    {
        readonly AsyncObservable<TValue> _source = source;
        readonly Func<TValue, TKey> _keySelector = keySelector;
        readonly Func<GroupedAsyncObservable<TKey, TValue>, CancellationToken, Task> _durationSelector = durationSelector;
        readonly Func<TKey, ISubject<TValue>> _groupSubjectSelector = groupSubjectSelector;

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<GroupedAsyncObservable<TKey, TValue>> observer, CancellationToken cancellationToken)
        {
            var subscription = new Subscription(this, observer);
            try
            {
                return await subscription.SubscribeAsync(cancellationToken);
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        sealed class Subscription(GroupByUntilAsyncObservable<TKey, TValue> parent, AsyncObserver<GroupedAsyncObservable<TKey, TValue>> observer) : AsyncObserver<TValue>
        {
            readonly Dictionary<TKey, Group> _groupsByKey = new();
            readonly CompositeAsyncDisposable _disposables = new();
            readonly object _groupsGate = new();
            bool _completed;

            public ValueTask<IAsyncDisposable> SubscribeAsync(CancellationToken cancellationToken)
            {
                return parent._source.SubscribeAsync(this, cancellationToken);
            }

            protected override async ValueTask OnNextAsyncCore(TValue value, CancellationToken cancellationToken)
            {
                var key = parent._keySelector(value);

                while (true)
                {
                    Group? group;
                    bool created;
                    lock (_groupsGate)
                    {
                        if (_completed)
                        {
                            return;
                        }

                        created = !_groupsByKey.TryGetValue(key, out group);
                        if (created)
                        {
                            group = new Group(this, key, parent._groupSubjectSelector(key));
                            _groupsByKey[key] = group;
                        }
                    }

                    if (created)
                    {
                        await observer.OnNextAsync(group!.Observable, cancellationToken);
                        var duration = parent._durationSelector(group.Observable, group.ClosedToken);
                        group.StartDuration(duration);
                    }

                    var delivered = await group!.OnNextAsync(value, cancellationToken);
                    if (delivered) break;
                }
            }

            void RemoveGroup(Group group)
            {
                lock (_groupsGate)
                {
                    if (_completed)
                    {
                        return;
                    }

                    if (_groupsByKey.TryGetValue(group.Key, out var current) && ReferenceEquals(current, group))
                    {
                        _groupsByKey.Remove(group.Key);
                    }
                }
            }

            protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                await observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override ValueTask OnCompletedAsyncCore(Result result) => CompleteAsync(result);

            async ValueTask CompleteAsync(Result result)
            {
                List<Group> groups;

                lock (_groupsGate)
                {
                    if (_completed)
                    {
                        return;
                    }

                    _completed = true;
                    groups = new List<Group>(_groupsByKey.Values);
                    _groupsByKey.Clear();

                }
                foreach (var group in groups)
                {
                    await group.CompleteAsync(result);
                }

                await observer.OnCompletedAsync(result);

                foreach (var group in groups)
                {
                    await group.Teardown();
                }
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                List<Group> groups;

                lock (_groupsGate)
                {
                    _completed = true;
                    groups = new List<Group>(_groupsByKey.Values);
                    _groupsByKey.Clear();
                }

                foreach (var group in groups)
                {
                    await group.Teardown();
                }

                await _disposables.DisposeAsync();
            }

            internal sealed class Group
            {
                readonly CancellationTokenSource _closedCts = new();
                private readonly AsyncGate _gate = new();
                public CancellationToken ClosedToken { get; }
                bool _closed;
                private readonly Subscription _parent;
                private readonly ISubject<TValue> _subject;

                public Group(Subscription parent, TKey key, ISubject<TValue> subject)
                {
                    _parent = parent;
                    _subject = subject;
                    Key = key;
                    Observable = new GroupObservable(parent, key, subject.Values);
                    ClosedToken = _closedCts.Token;
                }

                public TKey Key { get; }
                public GroupedAsyncObservable<TKey, TValue> Observable { get; }

                public async ValueTask<bool> OnNextAsync(TValue value, CancellationToken cancellationToken)
                {
                    using (await _gate.LockAsync())
                    {
                        if (_closed) return false;
                        await _subject.OnNextAsync(value, cancellationToken);
                    }

                    return true;
                }

                public async ValueTask CompleteAsync(Result result)
                {
                    _closedCts.Cancel();
                    using (await _gate.LockAsync())
                    {
                        if (_closed) return;
                        Close();
                    }

                    await _subject.OnCompletedAsync(result);
                }

                public async void StartDuration(Task duration)
                {
                    try
                    {
                        try
                        {
#if NET8_0_OR_GREATER
                            await duration.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
#else
                            await Task.Yield();
                            await duration;
#endif
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            UnhandledExceptionHandler.OnUnhandledException(exception);
                            return;
                        }

                        if (!_closedCts.IsCancellationRequested)
                        {
                            await CompleteAsync(Result.Success);
                        }
                    }
                    catch (Exception e)
                    {
                        UnhandledExceptionHandler.OnUnhandledException(e);
                    }
                }

                public async ValueTask Teardown()
                {
                    using (await _gate.LockAsync())
                    {
                        if (_closed) return;
                        Close();
                    }
                }

                void Close()
                {
                    _closed = true;
                    _closedCts.Cancel();
                    _parent.RemoveGroup(this);
                }
            }

            internal sealed class GroupObservable(Subscription parent, TKey key, AsyncObservable<TValue> subjectValues) : GroupedAsyncObservable<TKey, TValue>
            {
                protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TValue> observer, CancellationToken cancellationToken)
                {
                    var subscription = await subjectValues.SubscribeAsync(observer.Wrap(), cancellationToken);
                    await parent._disposables.AddAsync(subscription);
                    return AsyncDisposable.Create(async () =>
                    {
                        await parent._disposables.Remove(subscription);
                        await subscription.DisposeAsync();
                    });
                }

                public override TKey Key => key;
            }
        }
    }
}
