using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    /// <summary>
    /// Creates a resource with <paramref name="resourceFactory"/> for each subscription, uses it to build the observable via <paramref name="observableFactory"/>,
    /// and disposes the resource when that subscription is disposed.
    /// </summary>
    /// <typeparam name="T">The element type of the resulting sequence.</typeparam>
    /// <typeparam name="TResource">The type of the disposable resource shared with the observable factory.</typeparam>
    /// <param name="resourceFactory">Creates the resource, scoped to a single subscription.</param>
    /// <param name="observableFactory">Builds the observable to subscribe to given the created resource.</param>
    /// <remarks>If <paramref name="observableFactory"/> throws, the resource is disposed before the exception propagates.</remarks>
    public static AsyncObservable<T> Using<T, TResource>(Func<CancellationToken, ValueTask<TResource>> resourceFactory, Func<TResource, AsyncObservable<T>> observableFactory) where TResource : IAsyncDisposable
    {
        return Defer(async token =>
        {
            var resource = await resourceFactory(token);

            try
            {
                var observable = observableFactory(resource);
                return observable.OnDispose(resource.DisposeAsync);
            }
            catch
            {
                await resource.DisposeAsync();
                throw;
            }
        });
    }
}
