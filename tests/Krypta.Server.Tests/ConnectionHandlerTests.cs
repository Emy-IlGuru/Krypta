using System.Text.Json;
using System.Net.WebSockets;
using Krypta.Server.Connections;
using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Krypta.Server.Tests;

public sealed class ConnectionHandlerTests
{
    [Fact]
    public async Task Handle_FrameFrammentato_LoRicompattaEPulisceAllaChiusura()
    {
        RoomManager manager = new();
        ConnectionRegistry registry = new();
        ConnectionHandler handler = CreateHandler(manager, registry);
        FakeClientConnection connection = new();
        string json = SerializeClient(new JoinClientMessage(
            "K7X2M9",
            "emy",
            ValidPublicKey()));
        int splitAt = json.Length / 2;
        connection.QueueText(json[..splitAt], endOfMessage: false);
        connection.QueueText(json[splitAt..]);

        Task handling = handler.HandleAsync(connection, CancellationToken.None);
        await connection.WaitForSentCountAsync(3);
        connection.QueueClose();
        await handling;

        Assert.Contains(connection.Sent, payload =>
            DeserializeServer(payload) is JoinedServerMessage);
        Assert.Equal(0, manager.RoomCount);
        Assert.Equal(0, registry.Count);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, connection.OutputCloseStatus);
    }

    [Fact]
    public async Task Handle_DisconnessioneBrusca_EsegueIlCleanupNelFinally()
    {
        RoomManager manager = new();
        ConnectionRegistry registry = new();
        ConnectionHandler handler = CreateHandler(manager, registry);
        FakeClientConnection connection = new();
        connection.QueueText(SerializeClient(new JoinClientMessage(
            "K7X2M9",
            "emy",
            ValidPublicKey())));

        Task handling = handler.HandleAsync(connection, CancellationToken.None);
        await connection.WaitForSentCountAsync(3);
        connection.FailIncoming(new IOException("Connessione interrotta."));
        await handling;

        Assert.Equal(0, manager.RoomCount);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Handle_FrameOltreIlLimite_RispondeMalformedSenzaDeserializzarlo()
    {
        RoomManager manager = new();
        ConnectionRegistry registry = new();
        ConnectionHandler handler = CreateHandler(manager, registry);
        FakeClientConnection connection = new();
        connection.QueueText(new string('x', KryptaLimits.MaxFrameBytes + 1));

        Task handling = handler.HandleAsync(connection, CancellationToken.None);
        await connection.WaitForSentCountAsync(1);
        connection.QueueClose();
        await handling;
        ErrorServerMessage error = Assert.IsType<ErrorServerMessage>(
            DeserializeServer(Assert.Single(connection.Sent)));

        Assert.Equal(ErrorCodes.Malformed, error.Code);
        Assert.Equal(0, manager.RoomCount);
    }

    private static ConnectionHandler CreateHandler(
        RoomManager manager,
        ConnectionRegistry registry)
    {
        ProtocolMessageSender sender = new(manager);
        MessageDispatcher dispatcher = new(
            manager,
            sender,
            new CommandHandler(manager, sender, TimeProvider.System),
            new ActionRateLimiter(TimeProvider.System),
            new JoinRateLimiter(TimeProvider.System),
            TimeProvider.System);
        return new ConnectionHandler(
            registry,
            dispatcher,
            sender,
            NullLogger<ConnectionHandler>.Instance,
            TimeProvider.System);
    }

    private static string SerializeClient(ClientMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ClientMessage);

    private static ServerMessage DeserializeServer(string json) =>
        JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ServerMessage)!;

    private static string ValidPublicKey()
    {
        byte[] publicKey = new byte[KryptaLimits.PublicKeyBytes];
        publicKey[0] = 0x04;
        return Convert.ToBase64String(publicKey);
    }
}
