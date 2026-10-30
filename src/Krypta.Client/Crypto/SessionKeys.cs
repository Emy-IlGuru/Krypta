using System.Security.Cryptography;
using Krypta.Shared;

namespace Krypta.Client.Crypto;

/// <summary>
/// Coppia effimera ECDH P-256 della connessione. Nasce col socket e muore con lui: non
/// viene mai scritta su disco né loggata ([05 §5bis](../../../docs/05-client-desktop.md)).
/// </summary>
/// <remarks>
/// Qui c'è solo la generazione della coppia e la codifica della chiave pubblica, perché
/// il <c>join</c> non parte senza: il server valida il campo <c>publicKey</c> fin dalla
/// fase 1A. Derivazione del segreto, key wrapping e AES-GCM restano alla fase 1B.
/// </remarks>
public sealed class SessionKeys : IDisposable
{
    private const int CoordinateBytes = (KryptaLimits.PublicKeyBytes - 1) / 2;
    private const byte UncompressedPointTag = 0x04;

    private readonly ECDiffieHellman _Key;

    public SessionKeys()
    {
        _Key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        PublicKey = Convert.ToBase64String(ExportUncompressedPoint(_Key));
    }

    /// <summary>
    /// La chiave pubblica come punto X9.63 non compresso — 65 byte, in base64
    /// ([03 §7bis.9](../../../docs/03-protocollo.md)).
    /// </summary>
    public string PublicKey { get; }

    public void Dispose() => _Key.Dispose();

    /// <summary>
    /// <c>0x04</c> seguito da X e Y, 32 byte ciascuno, big-endian con zero-padding a
    /// sinistra. <b>Non</b> <c>ExportSubjectPublicKeyInfo()</c>, che produce un DER più
    /// lungo e diverso: due client che non concordano qui derivano segreti diversi senza
    /// che nessun errore lo segnali.
    /// </summary>
    private static byte[] ExportUncompressedPoint(ECDiffieHellman key)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        byte[] x = parameters.Q.X ?? throw new InvalidOperationException("Coordinata X assente.");
        byte[] y = parameters.Q.Y ?? throw new InvalidOperationException("Coordinata Y assente.");
        if (x.Length > CoordinateBytes || y.Length > CoordinateBytes)
        {
            throw new InvalidOperationException("Coordinate fuori misura per la curva P-256.");
        }

        byte[] point = new byte[KryptaLimits.PublicKeyBytes];
        point[0] = UncompressedPointTag;
        x.CopyTo(point, 1 + CoordinateBytes - x.Length);
        y.CopyTo(point, 1 + (2 * CoordinateBytes) - y.Length);
        return point;
    }
}
