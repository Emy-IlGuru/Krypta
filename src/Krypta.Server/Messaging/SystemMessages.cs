using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

/// <summary>
/// Costruisce i messaggi <c>system</c>. Il nickname lo mette sempre il server,
/// prendendolo dalla connessione: mai dal payload del client (07 §9, regola 6).
/// </summary>
internal static class SystemMessages
{
    public static SystemServerMessage RoomCreated(string nickname, DateTimeOffset at) =>
        new(SystemKinds.RoomCreated, $"{nickname} ha creato la stanza", at);

    public static SystemServerMessage JoinRequested(string nickname, DateTimeOffset at) =>
        new(SystemKinds.JoinRequested, $"{nickname} chiede di entrare nella stanza", at);

    public static SystemServerMessage UserJoined(
        string nickname,
        string approver,
        DateTimeOffset at) =>
        new(
            SystemKinds.UserJoined,
            $"{nickname} si è unito alla stanza, accettato da {approver}",
            at);

    public static SystemServerMessage JoinRejected(
        string approver,
        string nickname,
        DateTimeOffset at) =>
        new(SystemKinds.JoinRejected, $"{approver} ha rifiutato {nickname}", at);

    public static SystemServerMessage UserLeft(string nickname, DateTimeOffset at) =>
        new(SystemKinds.UserLeft, $"{nickname} è uscito dalla stanza", at);

    public static SystemServerMessage RoomRenamed(
        string nickname,
        string roomName,
        DateTimeOffset at) =>
        new(
            SystemKinds.RoomRenamed,
            $"{nickname} ha rinominato la stanza in «{roomName}»",
            at,
            RoomName: roomName);

    public static SystemServerMessage UserList(
        IReadOnlyList<string> users,
        DateTimeOffset at) =>
        new(
            SystemKinds.UserList,
            $"Partecipanti ({users.Count}/{KryptaLimits.MaxParticipantsPerRoom}): "
                + string.Join(", ", users),
            at,
            Users: users);

    public static SystemServerMessage RoomCleared(string nickname, DateTimeOffset at) =>
        new(SystemKinds.RoomCleared, $"{nickname} ha svuotato la stanza", at);
}
