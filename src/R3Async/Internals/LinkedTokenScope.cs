using System;
using System.Threading;

namespace R3Async.Internals;

/// <summary>
/// Wraps the effective <see cref="CancellationToken"/> for a single notification call, allocating a linked
/// <see cref="CancellationTokenSource"/> only when the caller-supplied token actually needs combining with the
/// observer's own dispose token. When the caller token is <see cref="CancellationToken.None"/> (the overwhelmingly
/// common case — most call sites don't pass a real per-call token), the dispose token is reused directly and no
/// allocation occurs.
/// </summary>
internal readonly struct LinkedTokenScope : IDisposable
{
    readonly CancellationTokenSource? _linkedCts;

    LinkedTokenScope(CancellationTokenSource? linkedCts, CancellationToken token)
    {
        _linkedCts = linkedCts;
        Token = token;
    }

    /// <summary>The effective cancellation token for the call.</summary>
    public CancellationToken Token { get; }

    /// <summary>
    /// Creates the scope for <paramref name="callerToken"/> against <paramref name="disposeToken"/>, skipping the
    /// linked-CTS allocation when <paramref name="callerToken"/> can't itself be canceled.
    /// </summary>
    public static LinkedTokenScope Create(CancellationToken callerToken, CancellationToken disposeToken)
    {
        if (!callerToken.CanBeCanceled)
            return new(null, disposeToken);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, disposeToken);
        return new(linkedCts, linkedCts.Token);
    }

    /// <inheritdoc/>
    public void Dispose() => _linkedCts?.Dispose();
}
