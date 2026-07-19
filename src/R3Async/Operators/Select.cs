using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Projects each value of the source sequence into a new form using an async <paramref name="selector"/>.
        /// </summary>
        /// <typeparam name="TDest">The type produced by <paramref name="selector"/>.</typeparam>
        /// <param name="selector">Async projection function applied to each value.</param>
        public AsyncObservable<TDest> Select<TDest>(Func<T, CancellationToken, ValueTask<TDest>> selector)
        {
            return new SelectAsyncObservable<T, TDest>(@this, selector);
        }

        /// <summary>
        /// Projects each value of the source sequence into a new form using a synchronous <paramref name="selector"/>.
        /// </summary>
        /// <typeparam name="TDest">The type produced by <paramref name="selector"/>.</typeparam>
        /// <param name="selector">Synchronous projection function applied to each value.</param>
        public AsyncObservable<TDest> Select<TDest>(Func<T, TDest> selector)
        {
            return new SelectObservable<T, TDest>(@this, selector);
        }
    }
}

sealed class SelectObservable<T, TDest>(AsyncObservable<T> source, Func<T, TDest> selector) : AsyncObservable<TDest>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TDest> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new SelectObserver(observer, selector), cancellationToken);
    }

    sealed class SelectObserver(AsyncObserver<TDest> observer, Func<T, TDest> selector) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
            => observer.OnNextAsync(selector(value), cancellationToken);

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}

sealed class SelectAsyncObservable<T, TDest>(AsyncObservable<T> source, Func<T, CancellationToken, ValueTask<TDest>> selector) : AsyncObservable<TDest>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TDest> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new SelectAsyncObserver(observer, selector), cancellationToken);
    }

    sealed class SelectAsyncObserver(AsyncObserver<TDest> observer, Func<T, CancellationToken, ValueTask<TDest>> selector) : AsyncObserver<T>
    {
        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            var mapped = await selector(value, cancellationToken);
            await observer.OnNextAsync(mapped, cancellationToken);
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}
