using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Subjects;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Partitions <paramref name="source"/> into groups keyed by <paramref name="keySelector"/>. Each distinct key
    /// produces a new <see cref="GroupedAsyncObservable{TKey, TValue}"/>, emitted the first time a value with that
    /// key is seen; each group is backed by a regular <see cref="Subject"/>.
    /// </summary>
    /// <remarks>
    /// Each group is a hot observable: it starts emitting values as soon as the source produces items for that
    /// key, regardless of whether anyone has subscribed to it yet. Subscribe to groups promptly after receiving
    /// them to avoid missing values, or use <see cref="GroupBy{TKey, TValue}(AsyncObservable{TValue}, Func{TValue, TKey}, Func{TKey, ISubject{TValue}})"/>
    /// with a replay/behavior subject selector if late subscription is expected.
    /// </remarks>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TValue">The type of the source and group values.</typeparam>
    /// <param name="source">The observable to partition.</param>
    /// <param name="keySelector">Computes the group key for each source value.</param>
    public static AsyncObservable<GroupedAsyncObservable<TKey, TValue>> GroupBy<TKey, TValue>(this AsyncObservable<TValue> source,
                                                                                              Func<TValue, TKey> keySelector)
        where TKey : notnull
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return new GroupByAsyncObservable<TKey, TValue>(source, keySelector, static _ => Subject.Create<TValue>());
    }

    /// <summary>
    /// Partitions <paramref name="source"/> into groups keyed by <paramref name="keySelector"/>, using
    /// <paramref name="groupSubjectSelector"/> to create the subject backing each group (e.g. a <c>BehaviorSubject</c>
    /// so late subscribers to a group receive its latest value instead of missing values emitted before they subscribed).
    /// </summary>
    /// <remarks>
    /// Each group is a hot observable: it starts emitting values as soon as the source produces items for that
    /// key, regardless of whether anyone has subscribed to it yet. Subscribe to groups promptly after receiving
    /// them to avoid missing values, unless <paramref name="groupSubjectSelector"/> is chosen to mitigate this
    /// (e.g. a behavior or replay subject).
    /// </remarks>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TValue">The type of the source and group values.</typeparam>
    /// <param name="source">The observable to partition.</param>
    /// <param name="keySelector">Computes the group key for each source value.</param>
    /// <param name="groupSubjectSelector">Creates the subject used to back each group, given its key.</param>
    public static AsyncObservable<GroupedAsyncObservable<TKey, TValue>> GroupBy<TKey, TValue>(this AsyncObservable<TValue> source,
                                                                                              Func<TValue, TKey> keySelector,
                                                                                              Func<TKey, ISubject<TValue>> groupSubjectSelector)
        where TKey : notnull
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return new GroupByAsyncObservable<TKey, TValue>(source, keySelector, groupSubjectSelector);
    }

    sealed class GroupByAsyncObservable<TKey, TValue>(AsyncObservable<TValue> source,
                                                      Func<TValue, TKey> keySelector,
                                                      Func<TKey, ISubject<TValue>> groupSubjectSelector) : AsyncObservable<GroupedAsyncObservable<TKey, TValue>>
        where TKey : notnull
    {
        readonly AsyncObservable<TValue> _source = source;
        readonly Func<TValue, TKey> _keySelector = keySelector;
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

        sealed class Subscription(GroupByAsyncObservable<TKey, TValue> parent, AsyncObserver<GroupedAsyncObservable<TKey, TValue>> observer) : AsyncObserver<TValue>
        {
            Dictionary<TKey, ISubject<TValue>> _subjectsByKey = new();
            readonly CompositeAsyncDisposable _disposables = new();

            public ValueTask<IAsyncDisposable> SubscribeAsync(CancellationToken cancellationToken)
            {
                return parent._source.SubscribeAsync(this, cancellationToken);
            }

            protected override async ValueTask OnNextAsyncCore(TValue value, CancellationToken cancellationToken)
            {
                var key = parent._keySelector(value);
                if (!_subjectsByKey.TryGetValue(key, out var subject))
                {
                    subject = parent._groupSubjectSelector(key);
                    _subjectsByKey.Add(key, subject);
                    await observer.OnNextAsync(new Observable(this, key, subject.Values), cancellationToken);
                }
                
                await subject.OnNextAsync(value, cancellationToken);
            }

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            {
                return observer.OnErrorResumeAsync(error, cancellationToken);
            }

            protected override async ValueTask OnCompletedAsyncCore(Result result)
            {
                var subjects = _subjectsByKey.Values;
                _subjectsByKey = null!;
                foreach (var subject in subjects)
                {
                    await subject.OnCompletedAsync(result);
                }

                await observer.OnCompletedAsync(result);
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                await base.DisposeAsyncCore();
                await _disposables.DisposeAsync();
            }

            internal class Observable(Subscription parent, TKey key, AsyncObservable<TValue> subjectValues) : GroupedAsyncObservable<TKey, TValue>
            {
                protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TValue> observer, CancellationToken cancellationToken)
                {
                    var subscription =  await subjectValues.SubscribeAsync(observer.Wrap(), cancellationToken);
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

/// <summary>An observable representing a single group produced by <c>GroupBy</c>, identified by <see cref="Key"/>.</summary>
/// <typeparam name="TKey">The type of the grouping key.</typeparam>
/// <typeparam name="TValue">The type of the values emitted by this group.</typeparam>
public abstract class GroupedAsyncObservable<TKey, TValue> : AsyncObservable<TValue>
{
    /// <summary>The key identifying this group, as produced by the <c>GroupBy</c> key selector.</summary>
    public abstract TKey Key { get; }
}
