using Krypta.Server.RateLimiting;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;

namespace Krypta.Server.Tests;

/// <summary>
/// Punto 1A.3: difesa dall'enumerazione degli ID stanza (04 §4.6).
/// </summary>
public sealed class JoinRateLimiterTests
{
    private const string Address = "192.168.1.31";

    [Fact]
    public void SestoTentativoIn3Minuti_Bloccato()
    {
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);

        for (int attempt = 1; attempt <= KryptaLimits.JoinRateLimitAttempts; attempt++)
        {
            Assert.True(limiter.TryJoin(Address, out _), $"tentativo {attempt}");
            time.AdvanceSeconds(1);
        }

        Assert.False(limiter.TryJoin(Address, out int retryAfter));
        Assert.Equal(KryptaLimits.JoinRateLimitBlockSeconds, retryAfter);
    }

    [Fact]
    public void ContaAncheITentativiRiusciti()
    {
        // È il punto centrale della difesa: contare solo i falliti renderebbe
        // l'enumerazione gratuita, perché chi scansiona riceve per lo più risposte valide.
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);

        for (int attempt = 0; attempt < KryptaLimits.JoinRateLimitAttempts; attempt++)
        {
            Assert.True(limiter.TryJoin(Address, out _));
        }

        Assert.False(limiter.TryJoin(Address, out _));
    }

    [Fact]
    public void DopoIlBlocco_TornaAConsentire()
    {
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);
        for (int attempt = 0; attempt < KryptaLimits.JoinRateLimitAttempts; attempt++)
        {
            limiter.TryJoin(Address, out _);
        }

        Assert.False(limiter.TryJoin(Address, out int retryAfter));
        time.AdvanceSeconds(retryAfter);

        Assert.True(limiter.TryJoin(Address, out _));
    }

    [Fact]
    public void IndirizziDiversi_NonSiInfluenzano()
    {
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);
        for (int attempt = 0; attempt < KryptaLimits.JoinRateLimitAttempts; attempt++)
        {
            limiter.TryJoin(Address, out _);
        }

        Assert.False(limiter.TryJoin(Address, out _));
        Assert.True(limiter.TryJoin("10.0.0.7", out _));
    }

    [Fact]
    public void FinestraScorrevole_TentativiVecchiNonContano()
    {
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);
        for (int attempt = 0; attempt < KryptaLimits.JoinRateLimitAttempts; attempt++)
        {
            limiter.TryJoin(Address, out _);
        }

        time.AdvanceSeconds(KryptaLimits.JoinRateLimitWindowSeconds + 1);

        Assert.True(limiter.TryJoin(Address, out _));
    }

    [Fact]
    public void Prune_RimuoveLeVociInattiveENonQuelleVive()
    {
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);
        limiter.TryJoin("10.0.0.1", out _);
        time.AdvanceSeconds(KryptaLimits.JoinRateLimitWindowSeconds + 1);
        limiter.TryJoin("10.0.0.2", out _);

        Assert.Equal(2, limiter.TrackedAddresses);
        Assert.Equal(1, limiter.Prune());
        Assert.Equal(1, limiter.TrackedAddresses);
    }

    [Fact]
    public void SenzaIndirizzo_NonLimitaNulla()
    {
        MovableTimeProvider time = new(ProtocolFixture.Now);
        JoinRateLimiter limiter = new(time);

        for (int attempt = 0; attempt < 50; attempt++)
        {
            Assert.True(limiter.TryJoin(null, out _));
        }

        Assert.Equal(0, limiter.TrackedAddresses);
    }
}
