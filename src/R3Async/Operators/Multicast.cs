using R3Async.Internals;
using R3Async.Subjects;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> source)
    {
        /// <summary>
        /// Converts <paramref name="source"/> into a hot, connectable observable that multicasts its values to all
        /// subscribers through <paramref name="subject"/>, sharing a single subscription to <paramref name="source"/>.
        /// Subscribers do not receive any values until <see cref="ConnectableAsyncObservable{T}.ConnectAsync"/> is called.
        /// </summary>
        /// <param name="subject">The subject used to relay values from <paramref name="source"/> to all subscribers.</param>
        public ConnectableAsyncObservable<T> Multicast(ISubject<T> subject) => new MulticastAsyncObservable<T>(source, subject);

        /// <summary>Equivalent to <c>Multicast(Subject.Create&lt;T&gt;())</c>: multicasts <paramref name="source"/> using a new regular <see cref="Subject"/>.</summary>
        public ConnectableAsyncObservable<T> Publish() => source.Multicast(Subject.Create<T>());

        /// <summary>Equivalent to <c>Multicast(Subject.Create&lt;T&gt;(options))</c>: multicasts <paramref name="source"/> using a new regular <see cref="Subject"/> with the given creation options.</summary>
        /// <param name="options">The options used to create the underlying subject.</param>
        public ConnectableAsyncObservable<T> Publish(SubjectCreationOptions options) => source.Multicast(Subject.Create<T>(options));

        /// <summary>Equivalent to <c>Multicast(Subject.CreateBehavior(initialValue))</c>: multicasts <paramref name="source"/> using a new <c>BehaviorSubject</c> that immediately emits <paramref name="initialValue"/> to new subscribers.</summary>
        /// <param name="initialValue">The value new subscribers receive before <paramref name="source"/> has produced any value.</param>
        public ConnectableAsyncObservable<T> Publish(T initialValue) => source.Multicast(Subject.CreateBehavior(initialValue));

        /// <summary>Equivalent to <c>Multicast(Subject.CreateBehavior(initialValue, options))</c>: multicasts <paramref name="source"/> using a new <c>BehaviorSubject</c> with the given creation options.</summary>
        /// <param name="initialValue">The value new subscribers receive before <paramref name="source"/> has produced any value.</param>
        /// <param name="options">The options used to create the underlying behavior subject.</param>
        public ConnectableAsyncObservable<T> Publish(T initialValue, BehaviorSubjectCreationOptions options) => source.Multicast(Subject.CreateBehavior(initialValue, options));

        /// <summary>Equivalent to <c>Multicast(Subject.CreateReplayLatest&lt;T&gt;())</c>: multicasts <paramref name="source"/> using a subject that replays the latest value to new subscribers once one has been emitted.</summary>
        public ConnectableAsyncObservable<T> ReplayLatestPublish() => source.Multicast(Subject.CreateReplayLatest<T>());

        /// <summary>Equivalent to <c>Multicast(Subject.CreateReplayLatest&lt;T&gt;(options))</c>: multicasts <paramref name="source"/> using a replay-latest subject with the given creation options.</summary>
        /// <param name="options">The options used to create the underlying replay-latest subject.</param>
        public ConnectableAsyncObservable<T> ReplayLatestPublish(ReplayLatestSubjectCreationOptions options) => source.Multicast(Subject.CreateReplayLatest<T>(options));
    }
}