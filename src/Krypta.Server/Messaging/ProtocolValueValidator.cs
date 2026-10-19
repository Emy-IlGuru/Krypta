using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

internal static class ProtocolValueValidator
{
    public static bool IsValidPublicKey(string? value) =>
        TryDecodeCanonicalBase64(value, KryptaLimits.PublicKeyBytes, out byte[] bytes)
        && bytes[0] == 0x04;

    public static bool IsValidWrappedKey(WrappedKey? wrappedKey) =>
        wrappedKey is not null
        && TryDecodeCanonicalBase64(
            wrappedKey.Epk,
            KryptaLimits.PublicKeyBytes,
            out byte[] epk)
        && epk[0] == 0x04
        && TryDecodeCanonicalBase64(
            wrappedKey.Nonce,
            KryptaLimits.NonceBytes,
            out _)
        && TryDecodeCanonicalBase64(
            wrappedKey.Ciphertext,
            KryptaLimits.WrappedKeyCiphertextBytes,
            out _);

    public static bool TryDecodeCanonicalBase64(
        string? value,
        int expectedBytes,
        out byte[] bytes)
    {
        int expectedCharacters = ((expectedBytes + 2) / 3) * 4;
        if (value is null || value.Length != expectedCharacters)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        return TryDecodeCanonicalBase64(value, out bytes)
            && bytes.Length == expectedBytes;
    }

    public static bool TryDecodeCanonicalBase64(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(value);
            if (!string.Equals(
                    Convert.ToBase64String(decoded),
                    value,
                    StringComparison.Ordinal))
            {
                return false;
            }

            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
