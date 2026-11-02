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
        /// <summary>
        /// Shares a single subscription to <paramref name="@this"/> among multiple observers, combining the
        /// behavior of <c>Publish()</c> and <c>RefCount()</c> using a regular <see cref="Subject"/>: the first
        /// subscriber connects the source, and by default the last unsubscribing observer disconnects it. See
        /// <paramref name="config"/> for how completion and reaching zero subscribers affect whether the connection is reset.
        /// </summary>
        /// <param name="config">Controls when the underlying connection is reset. Defaults to <see cref="ShareConfig.ResetOnCompletionAndRefCountZero"/>: the source is disconnected when the subscriber count drops to zero, and a completed connection is discarded so the next subscriber starts a fresh one. Pass <c>new ShareConfig()</c> to keep the source connected forever.</param>
        public AsyncObservable<T> Share(ShareConfig? config = null) => @this.Share(static () => Subject.Create<T>(),                       config);

        /// <summary>Same as <see cref="Share(AsyncObservable{T}, ShareConfig?)"/>, but uses a <c>BehaviorSubject</c> seeded with <paramref name="startValue"/> so new subscribers immediately receive the latest (or initial) value.</summary>
        /// <param name="startValue">The value emitted immediately to subscribers before the source has produced a value, or after the connection has been reset.</param>
        /// <param name="config">Controls when the underlying connection is reset. Defaults to <see cref="ShareConfig.ResetOnCompletionAndRefCountZero"/>.</param>
        public AsyncObservable<T> Share(T startValue, ShareConfig? config = null) => @this.Share(() => Subject.CreateBehavior(startValue), config);

        /// <summary>Same as <see cref="Share(AsyncObservable{T}, ShareConfig?)"/>, but uses a replay-latest subject so new subscribers immediately receive the most recently emitted value, if any.</summary>
        /// <param name="config">Controls when the underlying connection is reset. Defaults to <see cref="ShareConfig.ResetOnCompletionAndRefCountZero"/>.</param>
        public AsyncObservable<T> ShareLatest(ShareConfig? config = null) => @this.Share(Subject.CreateReplayLatest<T>,                    config);

        /// <summary>
        /// Shares a single subscription to <paramref name="@this"/> among multiple observers using a subject
        /// produced by <paramref name="connector"/>. <paramref name="connector"/> is invoked to create a fresh
        /// subject each time the connection is (re)established, including after a reset per <paramref name="config"/>.
        /// </summary>
        /// <param name="connector">Factory invoked to create the subject backing each connection.</param>
        /// <param name="config">Controls when the underlying connection is reset. Defaults to <see cref="ShareConfig.ResetOnCompletionAndRefCountZero"/>.</param>
        public AsyncObservable<T> Share(Func<ISubject<T>> connector, ShareConfig? config = null)
        {
            if (@this is null)
                throw new ArgumentNullException(nameof(@this));
            if (connector is null)
                throw new ArgumentNullException(nameof(connector));

            return new ShareObservable<T>(@this, connector, config ?? ShareConfig.ResetOnCompletionAndRefCountZero);
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
                    // Counted before subscribing: a replaying subject can run observers reentrantly.
                    _refCount++;
                    var connection = _connection;
                    if (connection is not null)
                    {
                        try
                        {
                            return await SubscribeToSubject(connection, observer, cancellationToken);
                        }
                        catch
                        {
                            _refCount--;
                            await ResetIfUnused(connection);
                            throw;
                        }
                    }

                    connection = new ShareConnection(this);
                    _connection = connection;
                    IAsyncDisposable disposable;
                    try
                    {
                        disposable = await SubscribeToSubject(connection, observer, cancellationToken);
                    }
                    catch
                    {
                        // Observers that joined reentrantly while this one was subscribing still need the source.
                        if (--_refCount == 0)
                        {
                            DropConnection(connection);
                        }
                        else
                        {
                            await ConnectSource(connection, cancellationToken);
                        }

                        throw;
                    }

                    try
                    {
                        await ConnectSource(connection, cancellationToken);
                    }
                    catch
                    {
                        await disposable.DisposeAsync();
                        throw;
                    }

                    return disposable;
                }
            }

            async ValueTask ConnectSource(ShareConnection connection, CancellationToken cancellationToken)
            {
                try
                {
                    await _parent._source.SubscribeAsync(connection, cancellationToken);
                }
                catch
                {
                    DropConnection(connection);
                    throw;
                }
            }

            void DropConnection(ShareConnection connection)
            {
                if (ReferenceEquals(_connection, connection))
                {
                    _connection = null;
                }
            }

            ValueTask ResetIfUnused(ShareConnection connection) =>
                _refCount == 0 && _parent._config.ResetOnRefCountZero && !connection.Completed ? DisposeConnection() : default;

            async ValueTask<IAsyncDisposable> SubscribeToSubject(ShareConnection connection, AsyncObserver<T> observer, CancellationToken cancellationToken)
            {
                var subscription = await connection.Subject.Values.SubscribeAsync(observer.Wrap(), cancellationToken);
                return AsyncDisposable.Create(async () =>
                {
                    using (await _gate.LockAsync())
                    {
                        _refCount--;
                        await subscription.DisposeAsync();
                        await ResetIfUnused(connection);
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

/// <summary>Configures when a <c>Share</c>/<c>ShareLatest</c> connection is reset (disposed and discarded, so a subsequent subscriber creates a brand-new connection and subject).</summary>
public sealed record ShareConfig
{
    /// <summary>A preconfigured config with <see cref="ResetOnSuccessResult"/>, <see cref="ResetOnErrorResult"/>, and <see cref="ResetOnRefCountZero"/> all set to <see langword="true"/>.</summary>
    public static ShareConfig ResetOnCompletionAndRefCountZero { get; } = new()
    {
        ResetOnSuccessResult = true,
        ResetOnErrorResult = true,
        ResetOnRefCountZero = true
    };

    /// <summary>When <see langword="true"/>, the connection is reset when the source completes with a failure result.</summary>
    public bool ResetOnErrorResult { get; init; }

    /// <summary>When <see langword="true"/>, the connection is reset when the source completes successfully.</summary>
    public bool ResetOnSuccessResult { get; init; }

    /// <summary>When <see langword="true"/>, the connection is reset as soon as the subscriber count drops to zero (and the source has not already completed).</summary>
    public bool ResetOnRefCountZero { get; init; }
}
