using R3Async.Internals;
using System;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Wraps <paramref name="observer"/> in a pass-through <see cref="AsyncObserver{T}"/> that forwards all notifications and disposal to it unchanged.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
    public static AsyncObserver<T> Wrap<T>(this AsyncObserver<T> observer)
    {
        return observer is null
            ? throw new ArgumentNullException(nameof(observer))
            : new WrappedAsyncObserver<T>(observer);
    }
}