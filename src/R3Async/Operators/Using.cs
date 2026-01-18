using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> Using<T, TResource>(Func<CancellationToken, ValueTask<TResource>> resourceFactory, Func<TResource, AsyncObservable<T>> observableFactory) where TResource : IAsyncDisposable
    {
        return Defer(async token =>
        {
            var resource = await resourceFactory(token);

            try
            {
                var observable = observableFactory(resource);
                return observable.OnDispose(async () =>
                {
                    await resource.DisposeAsync();
                });
            }
            catch
            {
                await resource.DisposeAsync();
                throw;
            }
        });
    }
}
