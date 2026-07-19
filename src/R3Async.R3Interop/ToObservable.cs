using System;
using System.Threading;
using System.Threading.Tasks;
using R3;

namespace R3Async.R3Interop;

public static class AsyncToR3ObservableExtensions
{
    /// <summary>
    /// Converts an <see cref="AsyncObservable{T}"/> into an R3 <see cref="Observable{T}"/>. Since R3's Subscribe and
    /// Dispose are synchronous while R3Async's are not, the configuration decides per operation how the async work is
    /// consumed: <see cref="AsyncToSyncStrategy.Blocking"/> blocks the caller until it completes (exceptions propagate),
    /// while <see cref="AsyncToSyncStrategy.FireAndForget"/> starts it without waiting and routes failures to its
    /// optional onException callback (or the <see cref="UnhandledExceptionHandler"/>).
    /// </summary>
    public static Observable<T> ToObservable<T>(this AsyncObservable<T> @this, ToObservableConfiguration configuration)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));

        return new AsyncToObservableAdapter<T>(@this, configuration);
    }

    sealed class AsyncToObservableAdapter<T>(AsyncObservable<T> source, ToObservableConfiguration configuration) : Observable<T>
    {
        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            return configuration.SubscribeStrategy.IsBlocking
                ? SubscribeBlocking(observer)
                : SubscribeFireAndForget(observer);
        }

        IDisposable SubscribeBlocking(Observer<T> observer)
        {
            var subscriptionTask = source.SubscribeAsync(new ObserverAdapter<T>(observer), CancellationToken.None);
            var subscription = subscriptionTask.IsCompletedSuccessfully
                ? subscriptionTask.GetAwaiter().GetResult()
                : subscriptionTask.AsTask().GetAwaiter().GetResult();

            return new SubscribedDisposable(subscription, configuration.DisposeStrategy);
        }

        IDisposable SubscribeFireAndForget(Observer<T> observer)
        {
            var cts = new CancellationTokenSource();

            ValueTask<IAsyncDisposable> subscriptionTask;
            try
            {
                subscriptionTask = source.SubscribeAsync(new ObserverAdapter<T>(observer), cts.Token).Preserve();
            }
            catch (Exception e)
            {
                subscriptionTask = new(Task.FromException<IAsyncDisposable>(e));
            }

            configuration.SubscribeStrategy.Execute(AwaitSubscription(subscriptionTask));

            return new PendingSubscriptionDisposable(subscriptionTask, cts, configuration.DisposeStrategy);
        }

        static async ValueTask AwaitSubscription(ValueTask<IAsyncDisposable> subscriptionTask) => await subscriptionTask;
    }

    sealed class ObserverAdapter<T>(Observer<T> observer) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            observer.OnNext(value);
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            observer.OnErrorResume(error);
            return default;
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            observer.OnCompleted(result.IsSuccess ? R3.Result.Success : R3.Result.Failure(result.Exception));
            return default;
        }
    }

}
