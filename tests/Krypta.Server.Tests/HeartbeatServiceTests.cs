using System.Text.Json;
using Krypta.Server.Connections;
using Krypta.Server.Messaging;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Krypta.Server.Tests;

public sealed class HeartbeatServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Heartbeat_ClientAttivo_RicevePing()
    {
        ConnectionRegistry registry = new();
        RoomManager manager = new();
        FakeClientConnection connection = new();
        using Participant participant = new(
            connection,
            timeProvider: new FixedTimeProvider(Now));
        registry.Register(participant);
        Task writer = participant.RunWriterAsync(CancellationToken.None);
        HeartbeatService heartbeat = CreateService(registry, manager);

        heartbeat.RunOnce();
        participant.CompleteOutgoing();
        await writer;

        ServerMessage sent = JsonSerializer.Deserialize(
            Assert.Single(connection.Sent),
            ProtocolJsonContext.Default.ServerMessage)!;
        Assert.IsType<PingServerMessage>(sent);
        Assert.False(participant.DisconnectRequested);
    }

    [Fact]
    public void Heartbeat_ClientMutoDaSessantaSecondi_RichiedeDisconnessione()
    {
        ConnectionRegistry registry = new();
        RoomManager manager = new();
        FakeClientConnection connection = new();
        using Participant participant = new(
            connection,
            timeProvider: new FixedTimeProvider(
                Now.AddSeconds(-KryptaLimits.ConnectionTimeoutSeconds)));
        registry.Register(participant);
        HeartbeatService heartbeat = CreateService(registry, manager);

        heartbeat.RunOnce();

        Assert.True(participant.DisconnectRequested);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, participant.DisconnectReason);
        Assert.Empty(connection.Sent);
    }

    private static HeartbeatService CreateService(
        ConnectionRegistry registry,
        RoomManager manager)
    {
        return new HeartbeatService(
            registry,
            new ProtocolMessageSender(manager),
            new FixedTimeProvider(Now),
            NullLogger<HeartbeatService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
