using System.Security.Cryptography;
using Krypta.Client.Chat;
using Krypta.Shared;

namespace Krypta.Client.Tests;

/// <summary>
/// Il ponte in chiaro della sola fase 1A. Sparisce con la 1B, ma finché c'è deve
/// rispettare i vincoli che il server applica all'involucro anche adesso.
/// </summary>
public sealed class ClearEnvelopeTests
{
    [Theory]
    [InlineData("ciao")]
    [InlineData("")]
    [InlineData("accentata: perché è così")]
    [InlineData("prima riga\nseconda riga")]
    [InlineData("emoji fuori dal BMP: \U0001F510")]
    public void Wrap_ERiletto_ConservaIlTestoEsatto(string text)
    {
        string ciphertext = ClearEnvelope.Wrap(text);

        Assert.True(ClearEnvelope.TryUnwrap(ClearEnvelope.ClearNonce, ciphertext, out string read));
        Assert.Equal(text, read);
    }

    [Fact]
    public void ClearNonce_EDodiciZeri()
    {
        byte[] nonce = Convert.FromBase64String(ClearEnvelope.ClearNonce);

        Assert.Equal(KryptaLimits.NonceBytes, nonce.Length);
        Assert.All(nonce, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ok")]
    [InlineData("un testo lungo abbastanza da non aver bisogno di riempimento")]
    public void Wrap_RispettaIVincoliDelServer(string text)
    {
        byte[] payload = Convert.FromBase64String(ClearEnvelope.Wrap(text));

        // Le stesse tre regole di ProtocolValueValidator: base64 canonico (già implicito
        // nel decode), oltre i 16 byte del tag, e sotto il tetto dell'involucro.
        Assert.True(
            payload.Length > KryptaLimits.GcmTagBytes,
            $"{payload.Length} byte: il server rifiuterebbe con INVALID_ENVELOPE.");
        Assert.True(payload.Length <= KryptaLimits.MaxCiphertextBytes);
    }

    [Fact]
    public void Wrap_TestoDellaLunghezzaMassima_StaDentroIlTettoDellInvolucro()
    {
        // 2000 caratteri che in UTF-8 pesano 3 byte ciascuno: il caso peggiore di §9.1.
        string text = new('\u20AC', KryptaLimits.MessageMaxLength);

        byte[] payload = Convert.FromBase64String(ClearEnvelope.Wrap(text));

        Assert.True(
            payload.Length <= KryptaLimits.MaxCiphertextBytes,
            $"{payload.Length} byte contro un tetto di {KryptaLimits.MaxCiphertextBytes}.");
    }

    [Fact]
    public void TryUnwrap_NonceCasuale_NonVieneMaiLettoComeTesto()
    {
        // È la proprietà che conta: un involucro davvero cifrato non deve poter finire
        // sullo schermo come se fosse leggibile.
        byte[] nonce = RandomNumberGenerator.GetBytes(KryptaLimits.NonceBytes);
        string ciphertext = ClearEnvelope.Wrap("testo che sembrerebbe leggibile");

        Assert.False(ClearEnvelope.TryUnwrap(
            Convert.ToBase64String(nonce),
            ciphertext,
            out string read));
        Assert.Equal(string.Empty, read);
    }

    [Theory]
    [InlineData("non-base64!")]
    [InlineData("AAA=")]
    public void TryUnwrap_InvolucroMalformato_Rifiuta(string ciphertext) =>
        Assert.False(ClearEnvelope.TryUnwrap(
            ClearEnvelope.ClearNonce,
            ciphertext,
            out _));

    [Fact]
    public void TryUnwrap_LunghezzaDichiarataOltreIDati_Rifiuta()
    {
        byte[] payload = new byte[24];
        payload[3] = 200;

        Assert.False(ClearEnvelope.TryUnwrap(
            ClearEnvelope.ClearNonce,
            Convert.ToBase64String(payload),
            out _));
    }
}
