using System.Text.Json.Serialization;

namespace Krypta.Shared.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JoinClientMessage), MessageTypes.Join)]
[JsonDerivedType(typeof(ChatClientMessage), MessageTypes.Message)]
[JsonDerivedType(typeof(CommandClientMessage), MessageTypes.Command)]
[JsonDerivedType(typeof(ApproveClientMessage), MessageTypes.Approve)]
[JsonDerivedType(typeof(RejectClientMessage), MessageTypes.Reject)]
[JsonDerivedType(typeof(RekeyClientMessage), MessageTypes.Rekey)]
[JsonDerivedType(typeof(CancelClientMessage), MessageTypes.Cancel)]
[JsonDerivedType(typeof(LeaveClientMessage), MessageTypes.Leave)]
[JsonDerivedType(typeof(PongClientMessage), MessageTypes.Pong)]
public abstract record ClientMessage;

public sealed record JoinClientMessage(
    string RoomId,
    string Nickname,
    string PublicKey,
    string? JoinMessage = null) : ClientMessage;

public sealed record ChatClientMessage(
    int Seq,
    int KeyEpoch,
    string Nonce,
    string Ciphertext) : ClientMessage;

public sealed record CommandClientMessage(
    string Name,
    string? Arg = null) : ClientMessage;

public sealed record ApproveClientMessage(
    Guid RequestId,
    WrappedKey WrappedKey,
    int KeyEpoch) : ClientMessage;

public sealed record RejectClientMessage(Guid RequestId) : ClientMessage;

public sealed record RekeyClientMessage(
    int KeyEpoch,
    IReadOnlyList<RekeyRecipient> Keys) : ClientMessage;

public sealed record CancelClientMessage : ClientMessage;

public sealed record LeaveClientMessage : ClientMessage;

public sealed record PongClientMessage : ClientMessage;
