using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

// Consecutive synchronous Select/Where calls are fused into a single observer hop.
delegate bool FusedStep<in TSource, TDest>(TSource value, out TDest result);

abstract class FusedObservable<TDest> : AsyncObservable<TDest>
{
    public abstract AsyncObservable<TNext> Select<TNext>(Func<TDest, TNext> selector);

    public abstract AsyncObservable<TDest> Where(Func<TDest, bool> predicate);
}

sealed class FusedObservable<TSource, TDest>(AsyncObservable<TSource> source, FusedStep<TSource, TDest> step) : FusedObservable<TDest>
{
    public override AsyncObservable<TNext> Select<TNext>(Func<TDest, TNext> selector) =>
        new FusedObservable<TSource, TNext>(source, (TSource value, out TNext result) =>
        {
            if (step(value, out var intermediate))
            {
                result = selector(intermediate);
                return true;
            }

            result = default!;
            return false;
        });

    public override AsyncObservable<TDest> Where(Func<TDest, bool> predicate) =>
        new FusedObservable<TSource, TDest>(source, (TSource value, out TDest result) => step(value, out result) && predicate(result));

    protected override ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<TDest> observer, CancellationToken cancellationToken)
    {
        return source.SubscribeAsync(new FusedObserver(observer, step), cancellationToken);
    }

    sealed class FusedObserver(AsyncObserver<TDest> observer, FusedStep<TSource, TDest> step) : AsyncObserver<TSource>
    {
        protected override ValueTask OnNextAsyncCore(TSource value, CancellationToken cancellationToken)
            => step(value, out var result) ? observer.OnNextAsync(result, cancellationToken) : default;

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
            => observer.OnErrorResumeAsync(error, cancellationToken);

        protected override ValueTask OnCompletedAsyncCore(Result result)
            => observer.OnCompletedAsync(result);
    }
}
