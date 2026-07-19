using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects;

/// <summary>
/// A hot, imperatively-controlled multicast source: values are pushed in by calling <see cref="OnNextAsync"/>,
/// <see cref="OnErrorResumeAsync"/> and <see cref="OnCompletedAsync"/>, and are forwarded to every observer currently
/// subscribed to <see cref="Values"/>. Unlike cold observables, a subject does not replay past notifications to new
/// subscribers (with the exception of behavior/replay-latest subjects created via <c>Subject.CreateBehavior</c> /
/// <c>Subject.CreateReplayLatest</c>), and once completed it rejects further notifications.
/// </summary>
public interface ISubject<T>
{
    /// <summary>The observable side of the subject; subscribers receive whatever is subsequently pushed via the <c>On*Async</c> methods.</summary>
    AsyncObservable<T> Values { get; }

    /// <summary>Pushes a value to all current subscribers. A no-op once the subject has completed.</summary>
    ValueTask OnNextAsync(T value, CancellationToken cancellationToken);

    /// <summary>Pushes a resumable error to all current subscribers without terminating the subject. A no-op once the subject has completed.</summary>
    ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken);

    /// <summary>Completes the subject with the given <paramref name="result"/>, notifying all current subscribers. Further notifications are ignored.</summary>
    ValueTask OnCompletedAsync(Result result);
}

/// <summary>Extension methods for adapting an <see cref="ISubject{T}"/> to the <see cref="AsyncObserver{T}"/> shape.</summary>
public static class SubjectExtensions
{
    /// <summary>Wraps the subject as an <see cref="AsyncObserver{T}"/>, so it can be used anywhere an observer is expected (e.g. as a subscription target).</summary>
    public static AsyncObserver<T> AsAsyncObserver<T>(this ISubject<T> subject) => new SubjectAsyncObserver<T>(subject);

    sealed class SubjectAsyncObserver<T>(ISubject<T> subject) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken) => subject.OnNextAsync(value, cancellationToken);
        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken) => subject.OnErrorResumeAsync(error, cancellationToken);
        protected override ValueTask OnCompletedAsyncCore(Result result) => subject.OnCompletedAsync(result);
    }
}