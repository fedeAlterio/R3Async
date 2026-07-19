using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;

namespace R3Async;

/// <summary>
/// Convenience overloads of <see cref="AsyncObservable{T}.SubscribeAsync"/> that accept lambda callbacks instead
/// of a full <see cref="AsyncObserver{T}"/> implementation. Each overload builds an anonymous observer and
/// delegates to the core subscribe method, so the same semantics apply: <c>cancellationToken</c> only guards the
/// subscribe operation itself, not the lifetime of the resulting stream (dispose the returned
/// <see cref="IAsyncDisposable"/> to unsubscribe).
/// </summary>
public static class AsyncObservableSubscribeExtensions
{
    extension<T>(AsyncObservable<T> source)
    {
        /// <summary>
        /// Subscribes with async callbacks for values, resumable errors, and completion. Any callback left
        /// <see langword="null"/> is simply not invoked for that notification.
        /// </summary>
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

        /// <summary>Subscribes with a synchronous callback invoked for each value; errors and completion are ignored.</summary>
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

        /// <summary>
        /// Subscribes with synchronous callbacks for values, resumable errors, and completion. Any callback left
        /// <see langword="null"/> is simply not invoked for that notification.
        /// </summary>
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

        /// <summary>
        /// Subscribes with an async callback for values and synchronous callbacks for resumable errors and
        /// completion. Any callback left <see langword="null"/> is simply not invoked for that notification.
        /// </summary>
        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync,
                                                          Action<Exception>? onErrorResume,
                                                          Action<Result>? onCompleted = null,
                                                          CancellationToken cancellationToken = default)
        {
            if (onNextAsync is null)
                throw new ArgumentNullException(nameof(onNextAsync));
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>(onNextAsync, onErrorResume is null ? null : (e, _) =>
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

        /// <summary>Subscribes with no callbacks, simply driving the stream and discarding all notifications.</summary>
        public ValueTask<IAsyncDisposable> SubscribeAsync()
        {
            return source.SubscribeAsync(static (_, _)  => default, CancellationToken.None);
        }

        /// <summary>Subscribes with an async callback for values only; errors and completion are ignored.</summary>
        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync)
        {
            return source.SubscribeAsync(onNextAsync, CancellationToken.None);
        }

        /// <summary>Subscribes with an async callback for values only; errors and completion are ignored.</summary>
        public ValueTask<IAsyncDisposable> SubscribeAsync(Func<T, CancellationToken, ValueTask> onNextAsync, CancellationToken cancellationToken)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var observer = new AnonymousAsyncObserver<T>(onNextAsync);
            return source.SubscribeAsync(observer, cancellationToken);
        }
    }
}