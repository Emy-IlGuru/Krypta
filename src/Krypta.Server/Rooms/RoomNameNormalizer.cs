using System.Text;
using Krypta.Shared;

namespace Krypta.Server.Rooms;

/// <summary>
/// Normalizza e valida il nome di una stanza (03 §6.1).
/// La normalizzazione degli spazi viene <b>prima</b> del controllo di lunghezza:
/// senza, "A" seguito da 24 spazi sarebbe un nome valido che appare vuoto.
/// </summary>
internal static class RoomNameNormalizer
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null)
        {
            return false;
        }

        StringBuilder builder = new(value.Length);
        bool pendingSpace = false;

        foreach (char character in value)
        {
            if (character == ' ')
            {
                // Gli spazi ai bordi spariscono, quelli interni si riducono a uno solo:
                // "Team    A" e "Team A" non devono essere due nomi diversi
                // indistinguibili sullo schermo.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (!IsAllowed(character))
            {
                return false;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        if (builder.Length is 0 or > KryptaLimits.RoomNameMaxLength)
        {
            return false;
        }

        normalized = builder.ToString();
        return true;
    }

    private static bool IsAllowed(char character) =>
        character is >= 'A' and <= 'Z'
        || character is >= 'a' and <= 'z'
        || character is >= '0' and <= '9';
}
