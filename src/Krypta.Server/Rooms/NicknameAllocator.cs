using Krypta.Shared;

namespace Krypta.Server.Rooms;

public static class NicknameAllocator
{
    private static readonly HashSet<string> ReservedNames = new(
        ["STANZA", "KRYPTA", "SISTEMA", "SYSTEM", "SERVER", "ROOM"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valida il nickname e restituisce l'unica normalizzazione consentita: il trim.
    /// </summary>
    public static bool TryNormalize(string? requested, out string? normalized)
    {
        normalized = null;
        if (requested is null)
        {
            return false;
        }

        string candidate = requested.Trim();
        if (candidate.Length is < KryptaLimits.NicknameMinLength
            or > KryptaLimits.NicknameMaxLength
            || ReservedNames.Contains(candidate)
            || !HasValidCharacters(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Restituisce il nickname libero più vicino a quello richiesto.
    /// </summary>
    public static string Allocate(string requested, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        if (!TryNormalize(requested, out string? normalized))
        {
            throw new ArgumentException("Il nickname richiesto non è valido.", nameof(requested));
        }

        string validNickname = normalized!;
        HashSet<string> occupied = new(taken, StringComparer.OrdinalIgnoreCase);
        if (!occupied.Contains(validNickname))
        {
            return validNickname;
        }

        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string suffixText = suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int baseLength = KryptaLimits.NicknameMaxLength - suffixText.Length;
            if (baseLength <= 0)
            {
                break;
            }

            string candidate = string.Concat(
                validNickname.AsSpan(0, Math.Min(validNickname.Length, baseLength)),
                suffixText);
            if (!occupied.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Non è disponibile un nickname valido.");
    }

    private static bool HasValidCharacters(string value)
    {
        bool previousWasSpace = false;

        foreach (char character in value)
        {
            bool isAsciiToken = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '.'
                or '-';

            if (isAsciiToken)
            {
                previousWasSpace = false;
                continue;
            }

            if (character != ' ' || previousWasSpace)
            {
                return false;
            }

            previousWasSpace = true;
        }

        return true;
    }
}
