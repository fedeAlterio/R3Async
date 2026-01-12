using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects;

public interface ISubject<T>
{
    AsyncObservable<T> Values { get; }
    ValueTask OnNextAsync(T value, CancellationToken cancellationToken);
    ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken);
    ValueTask OnCompletedAsync(Result result);
}

public static class SubjectExtensions
{
    public static AsyncObserver<T> AsAsyncObserver<T>(this ISubject<T> subject) => new SubjectAsyncObserver<T>(subject);

    sealed class SubjectAsyncObserver<T>(ISubject<T> subject) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken) => subject.OnNextAsync(value, cancellationToken);
        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken) => subject.OnErrorResumeAsync(error, cancellationToken);
        protected override ValueTask OnCompletedAsyncCore(Result result) => subject.OnCompletedAsync(result);
    }
}