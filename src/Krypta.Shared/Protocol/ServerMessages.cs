using System.Text.Json.Serialization;

namespace Krypta.Shared.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JoinedServerMessage), MessageTypes.Joined)]
[JsonDerivedType(typeof(ParticipantsServerMessage), MessageTypes.Participants)]
[JsonDerivedType(typeof(RosterServerMessage), MessageTypes.Roster)]
[JsonDerivedType(typeof(ChatServerMessage), MessageTypes.Message)]
[JsonDerivedType(typeof(SystemServerMessage), MessageTypes.System)]
[JsonDerivedType(typeof(ClearServerMessage), MessageTypes.Clear)]
[JsonDerivedType(typeof(LeftServerMessage), MessageTypes.Left)]
[JsonDerivedType(typeof(PingServerMessage), MessageTypes.Ping)]
[JsonDerivedType(typeof(ErrorServerMessage), MessageTypes.Error)]
[JsonDerivedType(typeof(PendingServerMessage), MessageTypes.Pending)]
[JsonDerivedType(typeof(WaitingServerMessage), MessageTypes.Waiting)]
[JsonDerivedType(typeof(RejectedServerMessage), MessageTypes.Rejected)]
[JsonDerivedType(typeof(RekeyedServerMessage), MessageTypes.Rekeyed)]
public abstract record ServerMessage;

public sealed record JoinedServerMessage(
    string RoomId,
    string RoomName,
    string Nickname,
    int Participants,
    bool Created,
    int MaxParticipants,
    int KeyEpoch,
    WrappedKey? WrappedKey = null) : ServerMessage;

public sealed record ParticipantsServerMessage(int Count) : ServerMessage;

public sealed record RosterServerMessage(
    int KeyEpoch,
    IReadOnlyList<RosterParticipant> Participants) : ServerMessage;

public sealed record ChatServerMessage(
    string Nickname,
    int Seq,
    int KeyEpoch,
    string Nonce,
    string Ciphertext,
    DateTimeOffset SentAt) : ServerMessage;

public sealed record SystemServerMessage(
    string Kind,
    string Text,
    DateTimeOffset SentAt,
    string? RoomName = null,
    IReadOnlyList<string>? Users = null) : ServerMessage;

public sealed record ClearServerMessage : ServerMessage;

public sealed record LeftServerMessage(string RoomId) : ServerMessage;

public sealed record PingServerMessage : ServerMessage;

public sealed record ErrorServerMessage(
    string Code,
    string Message,
    int? RetryAfterSeconds = null) : ServerMessage;

public sealed record PendingServerMessage(
    string RoomId,
    int ExpiresInSeconds) : ServerMessage;

public sealed record WaitingServerMessage(
    IReadOnlyList<WaitingRequest> Requests) : ServerMessage;

public sealed record RejectedServerMessage(
    string RoomId,
    string Reason) : ServerMessage;

public sealed record RekeyedServerMessage(
    int KeyEpoch,
    string By,
    WrappedKey WrappedKey) : ServerMessage;
