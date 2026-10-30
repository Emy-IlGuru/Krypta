using System.Buffers.Binary;
using System.Text;
using Krypta.Shared;

namespace Krypta.Client.Chat;

/// <summary>
/// <b>Ponte della sola fase 1A.</b> Il protocollo trasporta i messaggi dentro un involucro
/// cifrato, e il server ne valida la forma anche adesso: nonce di 12 byte, ciphertext di
/// almeno 17 (in 1B ci sarà dentro il tag GCM). La fase 1A è però dichiaratamente
/// **in chiaro**, quindi qualcosa in quei campi va messo lo stesso.
/// </summary>
/// <remarks>
/// <para>
/// Il nonce è <b>dodici zeri</b>, non un valore casuale: un nonce casuale farebbe sembrare
/// crittografia ciò che non lo è, e sarebbe indistinguibile da un involucro vero guardando
/// il traffico. Così invece è ovvio a occhio, ed è il marcatore su cui si regge
/// <see cref="TryUnwrap"/>: un involucro con nonce diverso da zero <b>non</b> viene letto
/// come testo, mai. È ciò che impedisce di mostrare come leggibile un messaggio davvero
/// cifrato.
/// </para>
/// <para>
/// <b>Tutto questo file sparisce con la fase 1B</b>, insieme alla riga della roadmap che lo
/// autorizza. Non è un formato del protocollo e non sta in [03](../../../docs/03-protocollo.md).
/// </para>
/// </remarks>
internal static class ClearEnvelope
{
    /// <summary>Il minimo che il server accetta: oltre i 16 byte del tag GCM.</summary>
    private const int MinimumPayloadBytes = KryptaLimits.GcmTagBytes + 1;

    private const int LengthPrefixBytes = 4;

    /// <summary>Dodici zeri in base64. Non è un nonce: è un'etichetta.</summary>
    public static string ClearNonce { get; } =
        Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]);

    /// <summary>
    /// Impacchetta il testo: quattro byte di lunghezza big-endian, i byte UTF-8, e zeri
    /// fino al minimo richiesto. Il prefisso evita di dover indovinare dove finisce il
    /// testo e dove comincia il riempimento.
    /// </summary>
    public static string Wrap(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text ?? string.Empty);
        int size = Math.Max(MinimumPayloadBytes, LengthPrefixBytes + utf8.Length);
        byte[] payload = new byte[size];

        BinaryPrimitives.WriteInt32BigEndian(payload, utf8.Length);
        utf8.CopyTo(payload, LengthPrefixBytes);

        return Convert.ToBase64String(payload);
    }

    /// <summary>
    /// Rilegge un involucro in chiaro. Restituisce <c>false</c> per qualunque cosa non sia
    /// stata scritta da <see cref="Wrap"/>, involucri veri compresi: in quel caso il
    /// chiamante deve mostrare la riga «non verificato», non il contenuto.
    /// </summary>
    public static bool TryUnwrap(string? nonce, string? ciphertext, out string text)
    {
        text = string.Empty;
        if (!string.Equals(nonce, ClearNonce, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(ciphertext ?? string.Empty);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length < LengthPrefixBytes)
        {
            return false;
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(payload);
        if (length < 0 || LengthPrefixBytes + length > payload.Length)
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(payload, LengthPrefixBytes, length);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
