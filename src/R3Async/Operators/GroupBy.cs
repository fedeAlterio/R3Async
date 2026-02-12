using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Subjects;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<GroupedAsyncObservable<TKey, TValue>> GroupBy<TKey, TValue>(this AsyncObservable<TValue> source,
                                                                                              Func<TValue, TKey> keySelector)
        where TKey : notnull
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return new GroupByAsyncObservable<TKey, TValue>(source, keySelector, static _ => Subject.Create<TValue>());
    }

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
            var subscrption = new Subscription(this, observer);
            try
            {
                return await subscrption.SubscribeAsync(cancellationToken);
            }
            catch
            {
                await subscrption.DisposeAsync();
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

public abstract class GroupedAsyncObservable<TKey, TValue> : AsyncObservable<TValue>
{
    public abstract TKey Key { get; }
}
