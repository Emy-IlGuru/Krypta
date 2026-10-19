using System.Text.Json;
using Krypta.Server.Rooms;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

public sealed class ProtocolMessageSender(RoomManager roomManager)
{
    public bool Send(Participant participant, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(message);
        return participant.TryEnqueue(Serialize(message));
    }

    public bool Broadcast(Room room, params ServerMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(messages);
        return roomManager.Broadcast(room, messages.Select(Serialize).ToArray());
    }

    public bool SendError(
        Participant participant,
        string code,
        int? retryAfterSeconds = null) =>
        Send(participant, new ErrorServerMessage(
            code,
            GetErrorMessage(code),
            retryAfterSeconds));

    public static string Serialize(ServerMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ServerMessage);

    private static string GetErrorMessage(string code) => code switch
    {
        ErrorCodes.InvalidRoomId => "L'ID stanza non è valido.",
        ErrorCodes.InvalidNickname => "Il nickname non è valido.",
        ErrorCodes.RoomFull => "La stanza ha già 20 partecipanti.",
        ErrorCodes.AlreadyInRoom => "La connessione è già in una stanza.",
        ErrorCodes.AlreadyPending => "La connessione è già in attesa.",
        ErrorCodes.WaitingRoomFull => "La sala d'attesa è piena.",
        ErrorCodes.UnknownRequest => "La richiesta non esiste più.",
        ErrorCodes.NotPending => "La connessione non è in attesa.",
        ErrorCodes.NotInRoom => "La connessione non è in una stanza.",
        ErrorCodes.MessageTooLong => "Il messaggio cifrato supera il limite consentito.",
        ErrorCodes.InvalidEnvelope => "L'involucro cifrato non è valido.",
        ErrorCodes.InvalidPublicKey => "La chiave pubblica non è valida.",
        ErrorCodes.InvalidJoinMessage => "Il messaggio di richiesta non è valido.",
        ErrorCodes.InvalidApproval => "L'approvazione non contiene una chiave valida.",
        ErrorCodes.InvalidRekey => "La rotazione della chiave non è valida.",
        ErrorCodes.StaleKeyEpoch => "L'epoca della chiave non è più corrente.",
        ErrorCodes.UnknownCommand => "Il comando non è riconosciuto.",
        ErrorCodes.Malformed => "Il messaggio ricevuto non è valido.",
        _ => "La richiesta non può essere completata."
    };
}
