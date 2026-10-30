using Krypta.Shared;

namespace Krypta.Server.Rooms;

public static class NicknameAllocator
{
    /// <summary>
    /// Valida il nickname e restituisce l'unica normalizzazione consentita: il trim.
    /// Le regole stanno in <see cref="NicknameRules"/>, condivise col client.
    /// </summary>
    public static bool TryNormalize(string? requested, out string? normalized) =>
        NicknameRules.TryNormalize(requested, out normalized);

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
}
