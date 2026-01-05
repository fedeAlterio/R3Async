using R3Async.Internals;
using System;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObserver<T> Wrap<T>(this AsyncObserver<T> observer)
    {
        return observer is null
            ? throw new ArgumentNullException(nameof(observer))
            : new WrappedAsyncObserver<T>(observer);
    }
}