using R3Async.Internals;
using R3Async.Subjects;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> source)
    {
        public ConnectableAsyncObservable<T> Multicast(ISubject<T> subject) => new MulticastAsyncObservable<T>(source, subject);
        
        public ConnectableAsyncObservable<T> Publish() => source.Multicast(Subject.Create<T>());
        public ConnectableAsyncObservable<T> Publish(SubjectCreationOptions options) => source.Multicast(Subject.Create<T>(options));

        public ConnectableAsyncObservable<T> Publish(T initialValue) => source.Multicast(Subject.CreateBehavior(initialValue));
        public ConnectableAsyncObservable<T> Publish(T initialValue, BehaviorSubjectCreationOptions options) => source.Multicast(Subject.CreateBehavior(initialValue, options));
    }
}