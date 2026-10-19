using Krypta.Server.Rooms;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

/// <summary>
/// Esegue i comandi di stanza. Non esistono ruoli: <b>ogni partecipante può eseguire ogni
/// comando</b> (ADR-0005). Il contrappeso è la tracciabilità — ogni comando che cambia
/// qualcosa annuncia chi lo ha eseguito, e il nickname lo mette il server.
/// </summary>
public sealed class CommandHandler(
    RoomManager roomManager,
    ProtocolMessageSender sender,
    TimeProvider timeProvider) : ICommandHandler
{
    public ValueTask HandleAsync(
        Participant participant,
        CommandClientMessage command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(command);

        Room? room = participant.CurrentRoom;
        if (room is null)
        {
            sender.SendError(participant, ErrorCodes.NotInRoom);
            return ValueTask.CompletedTask;
        }

        // 03 §6.0: il nome del comando è insensibile a maiuscole e minuscole.
        // Qui la tolleranza è innocua, al contrario che sull'ID stanza (ADR-0004):
        // un comando non riconosciuto produce un errore esplicito, non una stanza
        // nuova e vuota in cui l'utente si ritrova da solo senza accorgersene.
        if (Matches(command.Name, CommandNames.NameRoom))
        {
            HandleNameRoom(participant, room, command.Arg);
        }
        else if (Matches(command.Name, CommandNames.Users))
        {
            HandleUsers(room);
        }
        else if (Matches(command.Name, CommandNames.ClearRoom))
        {
            HandleClearRoom(participant, room);
        }
        else
        {
            // Ci finiscono anche "help", che è interamente lato client (03 §6.5),
            // e "generatekey", che viaggia come messaggio "rekey" e non come comando.
            sender.SendError(participant, ErrorCodes.UnknownCommand);
        }

        return ValueTask.CompletedTask;
    }

    private void HandleNameRoom(Participant participant, Room room, string? argument)
    {
        OperationResult result = roomManager.Rename(participant, argument);
        if (!result.Succeeded)
        {
            sender.SendError(participant, result.ErrorCode!);
            return;
        }

        // room.Name è il nome NORMALIZZATO: è quello che deve tornare ai client,
        // non l'argomento grezzo che il mittente ha digitato.
        sender.Broadcast(room, SystemMessages.RoomRenamed(
            participant.Nickname!,
            room.Name,
            timeProvider.GetUtcNow()));
    }

    private void HandleUsers(Room room) =>
        // Visibile a TUTTI, non solo a chi lo ha chiesto: è una scelta esplicita
        // (ADR-0006), così nessuno può controllare chi c'è di nascosto.
        sender.Broadcast(room, SystemMessages.UserList(
            roomManager.ListNicknames(room),
            timeProvider.GetUtcNow()));

    private void HandleClearRoom(Participant participant, Room room) =>
        // L'ordine è vincolante: prima "clear", poi la riga che dice chi è stato.
        // Al contrario, quella riga verrebbe cancellata subito dopo essere apparsa.
        sender.Broadcast(
            room,
            new ClearServerMessage(),
            SystemMessages.RoomCleared(
                participant.Nickname!,
                timeProvider.GetUtcNow()));

    private static bool Matches(string? name, string command) =>
        string.Equals(name, command, StringComparison.OrdinalIgnoreCase);
}
