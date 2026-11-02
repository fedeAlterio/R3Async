using R3Async.Internals;
using Shouldly;

namespace R3Async.Tests.Internals;

public class LinkedTokenCacheTests
{
    [Fact]
    public void Rent_ReusesTheLinkedTokenWhileTheCallerTokenIsTheSame()
    {
        using var dispose = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var cache = new LinkedTokenCache(dispose.Token);

        CancellationToken first, second;
        using (var lease = cache.Rent(caller.Token)) first = lease.Token;
        using (var lease = cache.Rent(caller.Token)) second = lease.Token;

        second.ShouldBe(first);
        first.ShouldNotBe(caller.Token);
        first.ShouldNotBe(dispose.Token);
    }

    [Fact]
    public void Rent_WithANonCancelableCallerTokenReturnsTheDisposeToken()
    {
        using var dispose = new CancellationTokenSource();
        var cache = new LinkedTokenCache(dispose.Token);

        using var lease = cache.Rent(CancellationToken.None);

        lease.Token.ShouldBe(dispose.Token);
    }

    [Fact]
    public void Rent_LinkedTokenFollowsBothTheCallerAndTheDisposeToken()
    {
        using var dispose = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var cache = new LinkedTokenCache(dispose.Token);

        using (var lease = cache.Rent(caller.Token))
        {
            caller.Cancel();
            lease.Token.IsCancellationRequested.ShouldBeTrue();
        }

        using var otherCaller = new CancellationTokenSource();
        using (var lease = cache.Rent(otherCaller.Token))
        {
            dispose.Cancel();
            lease.Token.IsCancellationRequested.ShouldBeTrue();
            otherCaller.IsCancellationRequested.ShouldBeFalse();
        }
    }

    [Fact]
    public void Rent_ReplacedTokenStillCancelsWhileAnOuterCallHoldsIt()
    {
        using var dispose = new CancellationTokenSource();
        using var outerCaller = new CancellationTokenSource();
        using var innerCaller = new CancellationTokenSource();
        var cache = new LinkedTokenCache(dispose.Token);

        using var outer = cache.Rent(outerCaller.Token);
        using (var inner = cache.Rent(innerCaller.Token))
        {
            inner.Token.ShouldNotBe(outer.Token);
        }

        outerCaller.Cancel();

        outer.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Rent_AfterDisposeReturnsTheCanceledDisposeToken()
    {
        using var dispose = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var cache = new LinkedTokenCache(dispose.Token);
        using (cache.Rent(caller.Token))
        {
        }

        dispose.Cancel();
        using var lease = cache.Rent(caller.Token);

        lease.Token.ShouldBe(dispose.Token);
        lease.Token.IsCancellationRequested.ShouldBeTrue();
    }
}
