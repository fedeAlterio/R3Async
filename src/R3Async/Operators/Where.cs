using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Filters the source sequence, forwarding only the values for which the async <paramref name="predicate"/> returns <see langword="true"/>.
        /// </summary>
        /// <param name="predicate">Async predicate evaluated for each value; values are dropped while it is awaited unless they pass.</param>
        public AsyncObservable<T> Where(Func<T, CancellationToken, ValueTask<bool>> predicate)
        {
            return new WhereAsyncObservable<T>(@this, predicate);
        }

        /// <summary>
        /// Filters the source sequence, forwarding only the values for which <paramref name="predicate"/> returns <see langword="true"/>.
        /// </summary>
        /// <param name="predicate">Synchronous predicate evaluated for each value.</param>
        public AsyncObservable<T> Where(Func<T, bool> predicate)
        {
            return new WhereObservable<T>(@this, predicate);
        }
    }
}

sealed class WhereObservable<T>(AsyncObservable<T> source, Func<T, bool> predicate) : AsyncObservable<T>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new WhereObserver(observer, predicate), cancellationToken);
    }

    sealed class WhereObserver(AsyncObserver<T> observer, Func<T, bool> predicate) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            => predicate(value) ? observer.OnNextAsync(value, cancellationToken) : default;

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}

sealed class WhereAsyncObservable<T>(AsyncObservable<T> source, Func<T, CancellationToken, ValueTask<bool>> predicate) : AsyncObservable<T>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new WhereAsyncObserver(observer, predicate), cancellationToken);
    }

    sealed class WhereAsyncObserver(AsyncObserver<T> observer, Func<T, CancellationToken, ValueTask<bool>> predicate) : AsyncObserver<T>
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            if (await predicate(value, cancellationToken))
            {
                await observer.OnNextAsync(value, cancellationToken);
            }
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}
