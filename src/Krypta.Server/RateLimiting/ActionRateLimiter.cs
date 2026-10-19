using Krypta.Server.Rooms;
using Krypta.Shared;

namespace Krypta.Server.RateLimiting;

/// <summary>
/// Limite di frequenza <b>per connessione</b>: 10 messaggi ogni 5 secondi e 3 comandi ogni
/// 10 secondi (04 §4.7). Il limite sui comandi conta soprattutto per <c>/ClearRoom</c> e
/// <c>/Users</c>, che generano traffico verso tutta la stanza.
/// </summary>
/// <remarks>
/// Lo stato vive sul <see cref="Participant"/> e muore con la connessione. Tenerlo qui in
/// un dizionario richiederebbe di ripulirlo alla disconnessione, e una voce dimenticata
/// sarebbe una perdita di memoria con superficie di attacco — lo stesso rischio che
/// 04 §4.6 segnala per il limite sui join, che invece per forza è per indirizzo.
/// </remarks>
public sealed class ActionRateLimiter(TimeProvider timeProvider)
{
    public bool TryMessage(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        return participant.MessageWindow.TryConsume(timeProvider.GetUtcNow(), out _);
    }

    public bool TryCommand(Participant participant, out int retryAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(participant);
        return participant.CommandWindow.TryConsume(
            timeProvider.GetUtcNow(),
            out retryAfterSeconds);
    }

    internal static SlidingWindow CreateMessageWindow() =>
        new(
            KryptaLimits.RateLimitMessages,
            TimeSpan.FromSeconds(KryptaLimits.RateLimitMessagesWindowSeconds));

    internal static SlidingWindow CreateCommandWindow() =>
        new(
            KryptaLimits.RateLimitCommands,
            TimeSpan.FromSeconds(KryptaLimits.RateLimitCommandsWindowSeconds));
}
