using Krypta.Client.Crypto;
using Krypta.Shared;

namespace Krypta.Client.Tests;

/// <summary>
/// La codifica della chiave pubblica è un accordo **fra client**: il server la tratta
/// come opaca e non può accorgersi di un disaccordo ([03 §7bis.9](../../docs/03-protocollo.md)).
/// </summary>
public sealed class SessionKeysTests
{
    [Fact]
    public void PublicKey_EUnPuntoX963NonCompressoDa65Byte()
    {
        using SessionKeys keys = new();

        byte[] point = Convert.FromBase64String(keys.PublicKey);

        Assert.Equal(KryptaLimits.PublicKeyBytes, point.Length);
        Assert.Equal(0x04, point[0]);
        Assert.Equal(88, keys.PublicKey.Length);
        Assert.StartsWith("B", keys.PublicKey, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicKey_DueSessioni_HannoChiaviDiverse()
    {
        // «Coppia effimera per connessione»: riusarla fra sessioni annullerebbe l'unica
        // cosa che rende la chiave di stanza non ricostruibile a posteriori.
        using SessionKeys first = new();
        using SessionKeys second = new();

        Assert.NotEqual(first.PublicKey, second.PublicKey);
    }

    [Fact]
    public void PublicKey_NonCambiaFraLetture()
    {
        using SessionKeys keys = new();

        Assert.Equal(keys.PublicKey, keys.PublicKey);
    }
}
