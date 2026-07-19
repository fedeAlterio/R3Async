using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        /// <summary>
        /// Applies an async accumulator function over the source sequence, emitting the running accumulated value for each source value.
        /// </summary>
        /// <typeparam name="TAcc">The type of the accumulated value.</typeparam>
        /// <param name="seed">The initial accumulator value.</param>
        /// <param name="accumulator">Async function combining the current accumulated value with the next source value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="accumulator"/> is <see langword="null"/>.</exception>
        public AsyncObservable<TAcc> Scan<TAcc>(TAcc seed, Func<TAcc, T, CancellationToken, ValueTask<TAcc>> accumulator)
        {
            if (accumulator is null) throw new ArgumentNullException(nameof(accumulator));

            return new ScanAsyncObservable<T, TAcc>(@this, seed, accumulator);
        }

        /// <summary>
        /// Applies a synchronous accumulator function over the source sequence, emitting the running accumulated value for each source value.
        /// </summary>
        /// <typeparam name="TAcc">The type of the accumulated value.</typeparam>
        /// <param name="seed">The initial accumulator value.</param>
        /// <param name="accumulator">Function combining the current accumulated value with the next source value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="accumulator"/> is <see langword="null"/>.</exception>
        public AsyncObservable<TAcc> Scan<TAcc>(TAcc seed, Func<TAcc, T, TAcc> accumulator)
        {
            if (accumulator is null) throw new ArgumentNullException(nameof(accumulator));

            return new ScanObservable<T, TAcc>(@this, seed, accumulator);
        }
    }
}

sealed class ScanObservable<T, TAcc>(AsyncObservable<T> source, TAcc seed, Func<TAcc, T, TAcc> accumulator) : AsyncObservable<TAcc>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TAcc> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new ScanObserver(observer, seed, accumulator), cancellationToken);
    }

    sealed class ScanObserver(AsyncObserver<TAcc> observer, TAcc seed, Func<TAcc, T, TAcc> accumulator) : AsyncObserver<T>
    {
        TAcc _acc = seed;

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            _acc = accumulator(_acc, value);
            return observer.OnNextAsync(_acc, cancellationToken);
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}

sealed class ScanAsyncObservable<T, TAcc>(AsyncObservable<T> source, TAcc seed, Func<TAcc, T, CancellationToken, ValueTask<TAcc>> accumulator) : AsyncObservable<TAcc>
{
    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TAcc> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new ScanAsyncObserver(observer, seed, accumulator), cancellationToken);
    }

    sealed class ScanAsyncObserver(AsyncObserver<TAcc> observer, TAcc seed, Func<TAcc, T, CancellationToken, ValueTask<TAcc>> accumulator) : AsyncObserver<T>
    {
        TAcc _acc = seed;

        protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            _acc = await accumulator(_acc, value, cancellationToken);
            await observer.OnNextAsync(_acc, cancellationToken);
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}
