using System.Text.Json.Serialization;

namespace Krypta.Shared.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ClientMessage))]
[JsonSerializable(typeof(ServerMessage))]
[JsonSerializable(typeof(JoinClientMessage))]
[JsonSerializable(typeof(ChatClientMessage))]
[JsonSerializable(typeof(CommandClientMessage))]
[JsonSerializable(typeof(ApproveClientMessage))]
[JsonSerializable(typeof(RejectClientMessage))]
[JsonSerializable(typeof(RekeyClientMessage))]
[JsonSerializable(typeof(CancelClientMessage))]
[JsonSerializable(typeof(LeaveClientMessage))]
[JsonSerializable(typeof(PongClientMessage))]
[JsonSerializable(typeof(JoinedServerMessage))]
[JsonSerializable(typeof(ParticipantsServerMessage))]
[JsonSerializable(typeof(RosterServerMessage))]
[JsonSerializable(typeof(ChatServerMessage))]
[JsonSerializable(typeof(SystemServerMessage))]
[JsonSerializable(typeof(ClearServerMessage))]
[JsonSerializable(typeof(LeftServerMessage))]
[JsonSerializable(typeof(PingServerMessage))]
[JsonSerializable(typeof(ErrorServerMessage))]
[JsonSerializable(typeof(PendingServerMessage))]
[JsonSerializable(typeof(WaitingServerMessage))]
[JsonSerializable(typeof(RejectedServerMessage))]
[JsonSerializable(typeof(RekeyedServerMessage))]
[JsonSerializable(typeof(WrappedKey))]
[JsonSerializable(typeof(MessageEnvelope))]
[JsonSerializable(typeof(RekeyRecipient))]
[JsonSerializable(typeof(RosterParticipant))]
[JsonSerializable(typeof(WaitingRequest))]
public partial class ProtocolJsonContext : JsonSerializerContext;
