using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static class AsyncToSystemObservableExtensions
{
    /// <summary>
    /// Converts an <see cref="AsyncObservable{T}"/> into a <see cref="IObservable{T}"/>. Since IObservable's Subscribe
    /// and Dispose are synchronous while R3Async's are not, the configuration decides per operation how the async work
    /// is consumed: <see cref="AsyncToSyncStrategy.Blocking"/> blocks the caller until it completes (exceptions
    /// propagate), while <see cref="AsyncToSyncStrategy.FireAndForget"/> starts it without waiting and routes failures
    /// to its optional onException callback (or the <see cref="UnhandledExceptionHandler"/>).
    /// The IObservable grammar has no resumable-error channel, so OnErrorResume terminates the sequence via OnError and
    /// tears down the subscription.
    /// </summary>
    public static IObservable<T> ToSystemObservable<T>(this AsyncObservable<T> @this, ToObservableConfiguration configuration)
    {
        if (@this is null)
            throw new ArgumentNullException(nameof(@this));

        return new AsyncToSystemObservableAdapter<T>(@this, configuration);
    }

    sealed class AsyncToSystemObservableAdapter<T>(AsyncObservable<T> source, ToObservableConfiguration configuration) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer is null)
                throw new ArgumentNullException(nameof(observer));

            return configuration.SubscribeStrategy.IsBlocking
                ? SubscribeBlocking(observer)
                : SubscribeFireAndForget(observer);
        }

        IDisposable SubscribeBlocking(IObserver<T> observer)
        {
            var subscriptionTask = source.SubscribeAsync(new ObserverAdapter<T>(observer), CancellationToken.None);
            var subscription = subscriptionTask.IsCompletedSuccessfully
                ? subscriptionTask.GetAwaiter().GetResult()
                : subscriptionTask.AsTask().GetAwaiter().GetResult();

            return new SubscribedDisposable(subscription, configuration.DisposeStrategy);
        }

        IDisposable SubscribeFireAndForget(IObserver<T> observer)
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

    sealed class ObserverAdapter<T>(IObserver<T> observer) : AsyncObserver<T>
    {
        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            observer.OnNext(value);
            return default;
        }

        protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            // IObservable<T> has no resumable-error channel: OnError is terminal, so the subscription must die with it.
            observer.OnError(error);
            await DisposeAsync();
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            if (result.IsSuccess)
            {
                observer.OnCompleted();
            }
            else
            {
                observer.OnError(result.Exception);
            }

            return default;
        }
    }
}
