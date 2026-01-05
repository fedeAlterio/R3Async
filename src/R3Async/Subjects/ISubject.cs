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