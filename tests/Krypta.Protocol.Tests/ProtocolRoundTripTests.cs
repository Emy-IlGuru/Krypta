using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Krypta.Shared.Protocol;

namespace Krypta.Protocol.Tests;

public sealed class ProtocolRoundTripTests
{
    private static readonly WrappedKey ExampleWrappedKey = new(
        "BM4tZ...==",
        "9jK1vQ8bZ2xR",
        "kR7d...==");

    [Fact]
    public void MessaggiClient_RoundTrip_PreservaTipoEContenutoJson()
    {
        Guid requestId = Guid.Parse("3f9a1c4e-1111-2222-3333-444444444444");

        (ClientMessage Message, string Type)[] messages =
        [
            (new JoinClientMessage("K7X2M9", "emy", "BEi3k9...==", "Posso entrare?"), MessageTypes.Join),
            (new ChatClientMessage(7, 2, "9jK1vQ8bZ2xR", "3pQ9xK2mA7...=="), MessageTypes.Message),
            (new CommandClientMessage(CommandNames.NameRoom, "Coordinamento"), MessageTypes.Command),
            (new ApproveClientMessage(requestId, ExampleWrappedKey, 2), MessageTypes.Approve),
            (new RejectClientMessage(requestId), MessageTypes.Reject),
            (new RekeyClientMessage(3, [new RekeyRecipient("luca", ExampleWrappedKey)]), MessageTypes.Rekey),
            (new CancelClientMessage(), MessageTypes.Cancel),
            (new LeaveClientMessage(), MessageTypes.Leave),
            (new PongClientMessage(), MessageTypes.Pong)
        ];

        foreach ((ClientMessage message, string type) in messages)
        {
            AssertRoundTrip(message, type, ProtocolJsonContext.Default.ClientMessage);
        }
    }

    [Fact]
    public void MessaggiServer_RoundTrip_PreservaTipoEContenutoJson()
    {
        DateTimeOffset sentAt = new(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);
        Guid requestId = Guid.Parse("3f9a1c4e-1111-2222-3333-444444444444");

        (ServerMessage Message, string Type)[] messages =
        [
            (new JoinedServerMessage("K7X2M9", "RM-K7X2M9", "emy2", 3, false, 20, 2, ExampleWrappedKey), MessageTypes.Joined),
            (new ParticipantsServerMessage(4), MessageTypes.Participants),
            (new RosterServerMessage(2, [new RosterParticipant("emy", "BBmcFyq...==")]), MessageTypes.Roster),
            (new ChatServerMessage("emy2", 7, 2, "9jK1vQ8bZ2xR", "3pQ9xK2mA7...==", sentAt), MessageTypes.Message),
            (new SystemServerMessage(
                SystemKinds.UserList,
                "Partecipanti (2/20): emy, luca",
                sentAt,
                Users: ["emy", "luca"]), MessageTypes.System),
            (new ClearServerMessage(), MessageTypes.Clear),
            (new LeftServerMessage("K7X2M9"), MessageTypes.Left),
            (new PingServerMessage(), MessageTypes.Ping),
            (new ErrorServerMessage(ErrorCodes.RoomFull, "La stanza ha gia 20 partecipanti."), MessageTypes.Error),
            (new PendingServerMessage("K7X2M9", 120), MessageTypes.Pending),
            (new WaitingServerMessage(
                [new WaitingRequest(requestId, "sara", "BEi3k9...==", 12, "Sono Sara")]), MessageTypes.Waiting),
            (new RejectedServerMessage("K7X2M9", RejectionReasons.Rejected), MessageTypes.Rejected),
            (new RekeyedServerMessage(3, "emy", ExampleWrappedKey), MessageTypes.Rekeyed)
        ];

        foreach ((ServerMessage message, string type) in messages)
        {
            AssertRoundTrip(message, type, ProtocolJsonContext.Default.ServerMessage);
        }
    }

    [Fact]
    public void CampiFacoltativiNull_NonVengonoScritti()
    {
        ClientMessage join = new JoinClientMessage("K7X2M9", "emy", "BEi3k9...==");
        ServerMessage system = new SystemServerMessage(
            SystemKinds.UserJoined,
            "emy si e unito alla stanza",
            new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero));

        string joinJson = JsonSerializer.Serialize(join, ProtocolJsonContext.Default.ClientMessage);
        string systemJson = JsonSerializer.Serialize(system, ProtocolJsonContext.Default.ServerMessage);

        Assert.DoesNotContain("joinMessage", joinJson, StringComparison.Ordinal);
        Assert.DoesNotContain("roomName", systemJson, StringComparison.Ordinal);
        Assert.DoesNotContain("users", systemJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CampoSconosciuto_VieneIgnorato()
    {
        const string json = """
            {
              "type": "join",
              "roomId": "K7X2M9",
              "nickname": "emy",
              "publicKey": "BEi3k9...==",
              "futureField": true
            }
            """;

        ClientMessage? message = JsonSerializer.Deserialize(
            json,
            ProtocolJsonContext.Default.ClientMessage);

        JoinClientMessage join = Assert.IsType<JoinClientMessage>(message);
        Assert.Equal("K7X2M9", join.RoomId);
    }

    private static void AssertRoundTrip<T>(T message, string expectedType, JsonTypeInfo<T> typeInfo)
    {
        string firstJson = JsonSerializer.Serialize(message, typeInfo);
        JsonObject firstObject = Assert.IsType<JsonObject>(JsonNode.Parse(firstJson));

        Assert.Equal(expectedType, firstObject["type"]?.GetValue<string>());
        Assert.DoesNotContain(firstObject, property => char.IsUpper(property.Key[0]));

        T? deserialized = JsonSerializer.Deserialize(firstJson, typeInfo);

        Assert.NotNull(deserialized);
        Assert.Equal(message!.GetType(), deserialized.GetType());

        string secondJson = JsonSerializer.Serialize(deserialized, typeInfo);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(firstJson), JsonNode.Parse(secondJson)),
            $"Il JSON non sopravvive al round-trip.{Environment.NewLine}Prima: {firstJson}{Environment.NewLine}Dopo: {secondJson}");
    }
}
