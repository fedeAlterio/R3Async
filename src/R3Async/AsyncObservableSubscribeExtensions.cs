using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

public static class AsyncObservableSubscribeExtensions
{
    extension<T>(AsyncObservable<T> source)
    {
        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync,
                                                          Func<Exception, CancellationToken, ValueTask>? onErrorResumeAsync = null,
                                                          Func<Result, ValueTask>? onCompletedAsync = null,
                                                          CancellationToken cancellationToken = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>(onNextAsync, onErrorResumeAsync, onCompletedAsync);
            return source.SubscribeAsync(observer, cancellationToken);
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync,
                                                          CancellationToken cancellationToken = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>(onNextAsync);
            return source.SubscribeAsync(observer, cancellationToken);
        }
    }
}