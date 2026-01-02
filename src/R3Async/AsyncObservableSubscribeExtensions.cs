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
                                                          Func<Exception, CancellationToken, ValueTask>? onErrorResumeAsync,
                                                          Func<Result, ValueTask>? onCompletedAsync = null,
                                                          CancellationToken cancellationToken = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>(onNextAsync, onErrorResumeAsync, onCompletedAsync);
            return source.SubscribeAsync(observer, cancellationToken);
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(Action<T> onNext, CancellationToken cancellationToken = default)
        {
            if (onNext is null)
                throw new ArgumentNullException(nameof(onNext));

            var observer = new AnonymousAsyncObserver<T>((x, _) =>
            {
                onNext(x);
                return default;
            });

            return source.SubscribeAsync(observer, cancellationToken);
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(Action<T> onNext,
                                                          Action<Exception>? onErrorResume = null,
                                                          Action<Result>? onCompleted = null,
                                                          CancellationToken cancellationToken = default)
        {
            if (onNext is null)
                throw new ArgumentNullException(nameof(onNext));
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>((x, _) =>
            {
                onNext(x);
                return default;
            }, onErrorResume is null ? null : (e, _) =>
            {
                onErrorResume(e);
                return default;
            }, onCompleted is null ? null : x =>
            {
                onCompleted(x);
                return default;
            });

            return source.SubscribeAsync(observer, cancellationToken);
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync()
        {
            return source.SubscribeAsync(static (_, _)  => default, CancellationToken.None);
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync)
        {
            return source.SubscribeAsync(onNextAsync, CancellationToken.None);
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync, CancellationToken cancellationToken)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>(onNextAsync);
            return source.SubscribeAsync(observer, cancellationToken);
        }
    }
}