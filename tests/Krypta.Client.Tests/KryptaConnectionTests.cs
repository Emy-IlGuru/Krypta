using System.Text.Json;
using Krypta.Client.Services;
using Krypta.Client.Tests.TestDoubles;
using Krypta.Shared.Protocol;

namespace Krypta.Client.Tests;

public sealed class KryptaConnectionTests
{
    [Fact]
    public async Task Connect_UsaEndpointWsESottoprotocolloV1()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);

        await connection.ConnectAsync("127.0.0.1", 5000, useTls: false);

        Assert.Equal(new Uri("ws://127.0.0.1:5000/ws"), transport.ConnectedUri);
        Assert.Equal(KryptaConnection.SubProtocol, transport.SubProtocol);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task Connect_SenzaTlsVersoHostRemoto_RifiutaPrimaDiAprireIlSocket()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ConnectAsync("192.168.1.42", 5000, useTls: false));

        Assert.Contains("127.0.0.1", error.Message, StringComparison.Ordinal);
        Assert.Null(transport.ConnectedUri);
    }

    [Fact]
    public async Task Join_InviaJsonDelContrattoSuUnSoloFrameTesto()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);
        await connection.ConnectAsync("127.0.0.1", 5000, useTls: false);

        await connection.JoinAsync("K7X2M9", "emy", "public-key", "fammi entrare");

        string json = Assert.Single(transport.SentMessages);
        ClientMessage? message = JsonSerializer.Deserialize(
            json,
            ProtocolJsonContext.Default.ClientMessage);
        JoinClientMessage join = Assert.IsType<JoinClientMessage>(message);
        Assert.Equal("K7X2M9", join.RoomId);
        Assert.Equal("fammi entrare", join.JoinMessage);
    }

    [Fact]
    public async Task Receive_FrameFrammentato_MarshalaEventoSulDispatcherUi()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);
        JoinedServerMessage? received = null;
        connection.Joined += (_, message) => received = message;
        await connection.ConnectAsync("127.0.0.1", 5000, useTls: false);
        JoinedServerMessage joined = new(
            "K7X2M9",
            "RM-K7X2M9",
            "emy",
            1,
            true,
            20,
            1);

        transport.QueueText(Serialize(joined), firstFragmentBytes: 7);
        await WaitUntilAsync(() => dispatcher.PendingCount > 0);

        Assert.Null(received);
        dispatcher.Drain();
        Assert.Equal(joined, received);
    }

    [Fact]
    public async Task Receive_Ping_RispondePongSenzaEventoUi()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);
        await connection.ConnectAsync("127.0.0.1", 5000, useTls: false);

        transport.QueueText(Serialize(new PingServerMessage()));
        await WaitUntilAsync(() => !transport.SentMessages.IsEmpty);

        string json = Assert.Single(transport.SentMessages);
        Assert.IsType<PongClientMessage>(JsonSerializer.Deserialize(
            json,
            ProtocolJsonContext.Default.ClientMessage));
        Assert.Equal(0, dispatcher.PendingCount);
    }

    [Fact]
    public async Task Receive_EventiPubblici_SonoTuttiAccodatiNelloStessoDispatcherUi()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);
        int eventCount = 0;
        connection.MessageReceived += (_, _) => eventCount++;
        connection.SystemReceived += (_, _) => eventCount++;
        connection.RoomRenamed += (_, _) => eventCount++;
        connection.ClearReceived += (_, _) => eventCount++;
        connection.ParticipantsChanged += (_, _) => eventCount++;
        connection.PendingStarted += (_, _) => eventCount++;
        connection.WaitingChanged += (_, _) => eventCount++;
        connection.Rejected += (_, _) => eventCount++;
        connection.ErrorReceived += (_, _) => eventCount++;
        await connection.ConnectAsync("127.0.0.1", 5000, useTls: false);

        transport.QueueText(Serialize(new ChatServerMessage(
            "emy", 1, 1, "nonce", "ciphertext", DateTimeOffset.UnixEpoch)));
        transport.QueueText(Serialize(new SystemServerMessage(
            SystemKinds.RoomRenamed,
            "Stanza rinominata",
            DateTimeOffset.UnixEpoch,
            RoomName: "Team")));
        transport.QueueText(Serialize(new ClearServerMessage()));
        transport.QueueText(Serialize(new ParticipantsServerMessage(2)));
        transport.QueueText(Serialize(new PendingServerMessage("K7X2M9", 120)));
        transport.QueueText(Serialize(new WaitingServerMessage([])));
        transport.QueueText(Serialize(new RejectedServerMessage("K7X2M9", "rejected")));
        transport.QueueText(Serialize(new ErrorServerMessage("TEST", "Errore di prova")));
        await WaitUntilAsync(() => dispatcher.PendingCount >= 8);

        Assert.Equal(0, eventCount);
        dispatcher.Drain();
        Assert.Equal(9, eventCount);
    }

    [Fact]
    public async Task Receive_ChiusuraImprevista_NotificaDisconnectedUnaSolaVoltaSulDispatcherUi()
    {
        FakeWebSocketTransport transport = new();
        QueuedUiDispatcher dispatcher = new();
        await using KryptaConnection connection = Create(transport, dispatcher);
        List<KryptaDisconnectedEventArgs> disconnections = [];
        connection.Disconnected += (_, args) => disconnections.Add(args);
        await connection.ConnectAsync("127.0.0.1", 5000, useTls: false);

        transport.QueueClose();
        await WaitUntilAsync(() => dispatcher.PendingCount > 0);

        Assert.Empty(disconnections);
        dispatcher.Drain();
        KryptaDisconnectedEventArgs disconnected = Assert.Single(disconnections);
        Assert.False(disconnected.Expected);
        Assert.False(connection.IsConnected);
    }

    private static KryptaConnection Create(
        FakeWebSocketTransport transport,
        QueuedUiDispatcher dispatcher) =>
        new(() => transport, dispatcher, TimeProvider.System);

    private static string Serialize(ServerMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ServerMessage);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
