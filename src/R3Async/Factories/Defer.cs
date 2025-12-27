using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Defer<T>(Func<CancellationToken, ValueTask<AsyncObservable<T>>> factory)
    {
        return Create<T>(async (observer, token) =>
        {
            var observable = await factory(token);
            return await observable.SubscribeAsync(observer.Wrap(), token);
        });
    }
}