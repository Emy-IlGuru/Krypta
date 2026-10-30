namespace Krypta.Shared;

/// <summary>Perché un nickname non è accettabile. Serve a dire *cosa* correggere.</summary>
public enum NicknameProblem
{
    None,
    Empty,
    TooShort,
    TooLong,
    Reserved,
    InvalidCharacters,
}

/// <summary>
/// Le regole del nickname, in un posto solo. Il client le applica per dare un errore
/// immediato mentre si digita; il server le riapplica comunque, perché un client
/// modificato invia quello che vuole (07 §9, regola 5). Due copie che divergono
/// darebbero un campo che il client accetta e il server rifiuta.
/// </summary>
public static class NicknameRules
{
    /// <summary>
    /// Nomi che il server rifiuta: senza il divieto un utente potrebbe chiamarsi
    /// <c>STANZA</c> e fabbricare falsi messaggi di sistema.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedNames =
        ["STANZA", "KRYPTA", "SISTEMA", "SYSTEM", "SERVER", "ROOM"];

    private static readonly HashSet<string> Reserved =
        new(ReservedNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valida e restituisce l'unica normalizzazione consentita: il trim. Non tocca le
    /// maiuscole — <c>Emy</c> ed <c>emy</c> restano nickname distinti — e non rimuove
    /// gli spazi interni.
    /// </summary>
    public static bool TryNormalize(string? requested, out string? normalized)
    {
        normalized = null;
        if (Validate(requested) is not NicknameProblem.None)
        {
            return false;
        }

        normalized = requested!.Trim();
        return true;
    }

    public static NicknameProblem Validate(string? requested)
    {
        string candidate = requested?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            return NicknameProblem.Empty;
        }

        if (candidate.Length < KryptaLimits.NicknameMinLength)
        {
            return NicknameProblem.TooShort;
        }

        if (candidate.Length > KryptaLimits.NicknameMaxLength)
        {
            return NicknameProblem.TooLong;
        }

        if (Reserved.Contains(candidate))
        {
            return NicknameProblem.Reserved;
        }

        return HasValidCharacters(candidate)
            ? NicknameProblem.None
            : NicknameProblem.InvalidCharacters;
    }

    /// <summary>
    /// Solo ASCII: il nickname finisce nell'AAD dei messaggi, e due forme Unicode
    /// identiche a vedersi ma diverse in byte farebbero fallire la decifratura
    /// (RF-S-12b). Lo spazio è ammesso solo singolo e interno.
    /// </summary>
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
