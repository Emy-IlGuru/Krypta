using System.Security.Cryptography;
using Krypta.Shared;

namespace Krypta.Client.Rooms;

/// <summary>
/// Genera un ID di stanza conforme, per il pulsante «Genera» della schermata [3]
/// (RF-C-07b). Serve alla regola «crea prima, condividi poi»: un ID concordato a voce
/// prima di aprire la stanza può essere occupato da un terzo, che diventa così il
/// creatore ([07 §4.3](../../../docs/07-sicurezza-e-privacy.md)).
/// </summary>
public static class RoomIdGenerator
{
    /// <summary>Le 36 lettere e cifre ammesse dal protocollo: `[A-Z0-9]`.</summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>
    /// <c>RandomNumberGenerator</c>, mai <c>Random</c>: un ID prevedibile è un ID
    /// indovinabile, ed è tutto ciò che serve per bussare a una stanza.
    /// </summary>
    public static string Next() =>
        RandomNumberGenerator.GetString(Alphabet, KryptaLimits.RoomIdLength);
}
