using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    public static AsyncObservable<T> FromAsync<T>(Func<CancellationToken, ValueTask<T>> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        return Create<T>((observer, _) =>
        {
            var cts = new CancellationTokenSource();
            AsyncLocal<bool> reentrant = new();
            Task task = Core(cts.Token);

            async Task Core(CancellationToken cancellationToken)
            {
                try
                {
                    reentrant.Value = true;
                    var result = await factory(cancellationToken);
                    await observer.OnNextAsync(result, cancellationToken);
                    await observer.OnCompletedAsync(Result.Success);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    try
                    {
                        await observer.OnCompletedAsync(Result.Failure(e));
                    }
                    catch (Exception exception)
                    {
                        UnhandledExceptionHandler.OnUnhandledException(exception);
                    }
                }
            }

            var subscription = AsyncDisposable.Create(async () =>
            {
                cts.Cancel();
                if (!reentrant.Value)
                {
                    await task;
                }

                cts.Dispose();
            });

            return new ValueTask<IAsyncDisposable>(subscription);
        });
    }
}
