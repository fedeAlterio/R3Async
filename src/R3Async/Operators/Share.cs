using R3Async.Internals;
using R3Async.Subjects;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async;

public static partial class AsyncObservable
{
    extension<T>(AsyncObservable<T> @this)
    {
        public AsyncObservable<T> Share(ShareConfig? config = null) => @this.Share(static () => Subject.Create<T>(),                       config);
        public AsyncObservable<T> Share(T startValue, ShareConfig? config = null) => @this.Share(() => Subject.CreateBehavior(startValue), config);
        public AsyncObservable<T> ShareLatest(ShareConfig? config = null) => @this.Share(Subject.CreateReplayLatest<T>,                    config);
        public AsyncObservable<T> Share(Func<ISubject<T>> connector, ShareConfig? config = null)
        {
            if (@this is null)
                throw new ArgumentNullException(nameof(@this));
            if (connector is null)
                throw new ArgumentNullException(nameof(connector));

            return new ShareObservable<T>(@this, connector, config ?? new ShareConfig());
        }
    }

    sealed class ShareObservable<T> : AsyncObservable<T>
    {
        readonly AsyncObservable<T> _source;
        readonly Func<ISubject<T>> _connector;
        readonly ShareConfig _config;
        readonly ShareSubscription _share;

        public ShareObservable(AsyncObservable<T> source, Func<ISubject<T>> connector, ShareConfig config)
        {
            _source = source;
            _connector = connector;
            _config = config;
            _share = new(this);
        }

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            return await _share.SubscribeAsyncCore(observer, cancellationToken);
        }

        sealed class ShareSubscription(ShareObservable<T> parent)
        {
            readonly ShareObservable<T> _parent = parent;
            readonly AsyncGate _gate = new();
            ShareConnection? _connection;

            int _refCount;
            public async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
            {
                using (await _gate.LockAsync())
                {
                    _refCount++;

                    var connection = _connection;
                    if (connection is null)
                    {
                        connection = new ShareConnection(this);
                        _connection = connection;
                        await _parent._source.SubscribeAsync(connection, cancellationToken);
                    }

                    var disposable = await SubscribeToSubject(connection, observer, cancellationToken);
                    return disposable;
                }
            }

            async ValueTask<IAsyncDisposable> SubscribeToSubject(ShareConnection connection, AsyncObserver<T> observer, CancellationToken cancellationToken)
            {
                var subscription = await connection.Subject.Values.SubscribeAsync(observer.Wrap(), cancellationToken);
                return AsyncDisposable.Create(async () =>
                {
                    using (await _gate.LockAsync())
                    {
                        var refCount = --_refCount;
                        await subscription.DisposeAsync();
                        if (_parent._config.ResetOnRefCountZero && refCount == 0 && !connection.Completed)
                        {
                            await DisposeConnection();
                        }
                    }
                });
            }


            ValueTask DisposeConnection()
            {
                var connection = _connection;
                if (connection is null) return default;
                _connection = null;
                return connection.DisposeAsync();
            }

            sealed class ShareConnection(ShareSubscription parent) : AsyncObserver<T>
            {
                public readonly ISubject<T> Subject = parent._parent._connector();
                public bool Completed;

                protected override async ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
                {
                    await Subject.OnNextAsync(value, cancellationToken);
                }

                protected override async ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
                {
                    await Subject.OnErrorResumeAsync(error, cancellationToken);
                }

                protected override async ValueTask OnCompletedAsyncCore(Result result)
                {
                    using (await parent._gate.LockAsync())
                    {
                        Completed = true;
                        var config = parent._parent._config;
                        if (result.IsSuccess)
                        {
                            if (config.ResetOnSuccessResult)
                            {
                                await parent.DisposeConnection();
                            }
                        }
                        else
                        {
                            if (config.ResetOnErrorResult)
                            {
                                await parent.DisposeConnection();
                            }
                        }

                        await Subject.OnCompletedAsync(result);
                    }
                }
            }
        }
    }
}

public sealed record ShareConfig
{
    public static ShareConfig ResetOnCompletionAndRefCountZero { get; } = new()
    {
        ResetOnSuccessResult = true,
        ResetOnErrorResult = true,
        ResetOnRefCountZero = true
    };

    public bool ResetOnErrorResult { get; init; }
    public bool ResetOnSuccessResult { get; init; }
    public bool ResetOnRefCountZero { get; init; }
}
