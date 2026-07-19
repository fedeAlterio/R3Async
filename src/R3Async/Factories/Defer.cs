using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that defers creation of the actual source until subscription time,
    /// by invoking <paramref name="factory"/> for each new subscriber and subscribing to the observable it asynchronously produces.
    /// This allows subscription-time state (or async setup) to influence which observable is used, and gives each
    /// subscriber an independently created source.
    /// </summary>
    public static AsyncObservable<T> Defer<T>(Func<CancellationToken, ValueTask<AsyncObservable<T>>> factory)
    {
        return Create<T>(async (observer, token) =>
        {
            var observable = await factory(token);
            return await observable.SubscribeAsync(observer.Wrap(), token);
        });
    }

    /// <summary>
    /// Creates an <see cref="AsyncObservable{T}"/> that defers creation of the actual source until subscription time,
    /// by invoking <paramref name="factory"/> for each new subscriber and subscribing to the observable it produces.
    /// Each subscriber gets an independently created source.
    /// </summary>
    public static AsyncObservable<T> Defer<T>(Func<AsyncObservable<T>> factory)
    {
        return Create<T>(async (observer, token) =>
        {
            var observable = factory();
            return await observable.SubscribeAsync(observer.Wrap(), token);
        });
    }
}