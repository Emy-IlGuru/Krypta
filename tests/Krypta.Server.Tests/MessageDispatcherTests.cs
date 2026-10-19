using System.Text.Json;
using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests;

public sealed class MessageDispatcherTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Join_StanzaNuova_InviaJoinedParticipantsERoster()
    {
        RoomManager manager = new(new FixedTimeProvider(FixedNow));
        TestCommandHandler commands = new();
        MessageDispatcher dispatcher = CreateDispatcher(manager, commands);
        TestClient client = new();

        await dispatcher.DispatchAsync(
            client.Participant,
            SerializeClient(new JoinClientMessage(
                "K7X2M9",
                "emy",
                ValidPublicKey())),
            CancellationToken.None);
        ServerMessage[] sent = await client.FinishAsync();

        JoinedServerMessage joined = Assert.Single(sent.OfType<JoinedServerMessage>());
        Assert.True(joined.Created);
        Assert.Equal("emy", joined.Nickname);
        Assert.Equal(1, joined.KeyEpoch);
        Assert.Null(joined.WrappedKey);
        Assert.Equal(1, Assert.Single(sent.OfType<ParticipantsServerMessage>()).Count);
        RosterParticipant rosterEntry = Assert.Single(
            Assert.Single(sent.OfType<RosterServerMessage>()).Participants);
        Assert.Equal("emy", rosterEntry.Nickname);
        Assert.Equal(ValidPublicKey(), rosterEntry.PublicKey);
    }

    [Fact]
    public async Task Join_StanzaEsistente_InviaPendingAlRichiedenteEWaitingAiPresenti()
    {
        RoomManager manager = new(new FixedTimeProvider(FixedNow));
        MessageDispatcher dispatcher = CreateDispatcher(manager, new TestCommandHandler());
        TestClient owner = new();
        TestClient requester = new();
        manager.Join("K7X2M9", "owner", ValidPublicKey(), owner.Participant);

        await dispatcher.DispatchAsync(
            requester.Participant,
            SerializeClient(new JoinClientMessage(
                "K7X2M9",
                "luca",
                ValidPublicKey(),
                "Sono Luca")),
            CancellationToken.None);
        ServerMessage[] ownerMessages = await owner.FinishAsync();
        ServerMessage[] requesterMessages = await requester.FinishAsync();

        PendingServerMessage pending = Assert.Single(
            requesterMessages.OfType<PendingServerMessage>());
        Assert.Equal("K7X2M9", pending.RoomId);
        Assert.Equal(KryptaLimits.JoinApprovalTimeoutSeconds, pending.ExpiresInSeconds);
        WaitingRequest request = Assert.Single(
            Assert.Single(ownerMessages.OfType<WaitingServerMessage>()).Requests);
        Assert.Equal("luca", request.Nickname);
        Assert.Equal(ValidPublicKey(), request.PublicKey);
        Assert.Equal("Sono Luca", request.JoinMessage);
        Assert.DoesNotContain(requesterMessages, message => message is WaitingServerMessage);
    }

    [Fact]
    public async Task Message_Valido_RaggiungeSoloLaStanzaConNicknameDelServer()
    {
        RoomManager manager = new(new FixedTimeProvider(FixedNow));
        MessageDispatcher dispatcher = CreateDispatcher(manager, new TestCommandHandler());
        TestClient owner = new();
        TestClient member = new();
        TestClient pending = new();
        TestClient otherRoom = new();
        Room room = manager.Join(
            "K7X2M9",
            "owner",
            ValidPublicKey(),
            owner.Participant).Room!;
        JoinResult memberRequest = manager.Join(
            "K7X2M9",
            "member",
            ValidPublicKey(),
            member.Participant);
        manager.Approve(owner.Participant, memberRequest.RequestId!.Value, ValidWrappedKey(), 1);
        manager.Join("K7X2M9", "pending", ValidPublicKey(), pending.Participant);
        manager.Join("ABC123", "other", ValidPublicKey(), otherRoom.Participant);
        ChatClientMessage clientMessage = new(
            7,
            1,
            Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]),
            Convert.ToBase64String(new byte[KryptaLimits.GcmTagBytes + 1]));

        await dispatcher.DispatchAsync(
            owner.Participant,
            SerializeClient(clientMessage),
            CancellationToken.None);
        ServerMessage[] ownerMessages = await owner.FinishAsync();
        ServerMessage[] memberMessages = await member.FinishAsync();
        ServerMessage[] pendingMessages = await pending.FinishAsync();
        ServerMessage[] otherMessages = await otherRoom.FinishAsync();

        ChatServerMessage sent = Assert.Single(ownerMessages.OfType<ChatServerMessage>());
        Assert.Equal("owner", sent.Nickname);
        Assert.Equal(clientMessage.Seq, sent.Seq);
        Assert.Equal(clientMessage.KeyEpoch, sent.KeyEpoch);
        Assert.Equal(clientMessage.Nonce, sent.Nonce);
        Assert.Equal(clientMessage.Ciphertext, sent.Ciphertext);
        Assert.Equal(FixedNow, sent.SentAt);
        Assert.Equal(sent, Assert.Single(memberMessages.OfType<ChatServerMessage>()));
        Assert.Empty(pendingMessages);
        Assert.Empty(otherMessages);
        Assert.Equal(2, room.ParticipantCount);
    }

    [Fact]
    public async Task Message_CiphertextOltreIlLimite_RestituisceMessageTooLong()
    {
        RoomManager manager = new();
        MessageDispatcher dispatcher = CreateDispatcher(manager, new TestCommandHandler());
        TestClient client = new();
        manager.Join("K7X2M9", "emy", ValidPublicKey(), client.Participant);
        ChatClientMessage message = new(
            1,
            1,
            Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]),
            Convert.ToBase64String(new byte[KryptaLimits.MaxCiphertextBytes + 1]));

        await dispatcher.DispatchAsync(
            client.Participant,
            SerializeClient(message),
            CancellationToken.None);
        ErrorServerMessage error = Assert.Single(
            (await client.FinishAsync()).OfType<ErrorServerMessage>());

        Assert.Equal(ErrorCodes.MessageTooLong, error.Code);
    }

    [Fact]
    public async Task Approve_Valido_ConsegnaWrappedKeyEAllineaLaStanza()
    {
        RoomManager manager = new(new FixedTimeProvider(FixedNow));
        MessageDispatcher dispatcher = CreateDispatcher(manager, new TestCommandHandler());
        TestClient owner = new();
        TestClient requester = new();
        manager.Join("K7X2M9", "owner", ValidPublicKey(), owner.Participant);
        JoinResult pending = manager.Join(
            "K7X2M9",
            "luca",
            ValidPublicKey(),
            requester.Participant);
        WrappedKey wrappedKey = ValidWrappedKey();

        await dispatcher.DispatchAsync(
            owner.Participant,
            SerializeClient(new ApproveClientMessage(
                pending.RequestId!.Value,
                wrappedKey,
                1)),
            CancellationToken.None);
        ServerMessage[] ownerMessages = await owner.FinishAsync();
        ServerMessage[] requesterMessages = await requester.FinishAsync();

        JoinedServerMessage joined = Assert.Single(
            requesterMessages.OfType<JoinedServerMessage>());
        Assert.False(joined.Created);
        Assert.Equal(wrappedKey, joined.WrappedKey);
        Assert.Equal(2, joined.Participants);
        Assert.Contains(ownerMessages,
            message => message is ParticipantsServerMessage { Count: 2 });
        Assert.Contains(ownerMessages,
            message => message is WaitingServerMessage { Requests.Count: 0 });
        Assert.Contains(requesterMessages, message => message is RosterServerMessage);
    }

    [Fact]
    public async Task Leave_Valido_ConfermaAlMittenteEAggiornaGliAltri()
    {
        RoomManager manager = new();
        MessageDispatcher dispatcher = CreateDispatcher(manager, new TestCommandHandler());
        TestClient owner = new();
        TestClient member = new();
        manager.Join("K7X2M9", "owner", ValidPublicKey(), owner.Participant);
        JoinResult pending = manager.Join(
            "K7X2M9",
            "member",
            ValidPublicKey(),
            member.Participant);
        manager.Approve(owner.Participant, pending.RequestId!.Value, ValidWrappedKey(), 1);

        await dispatcher.DispatchAsync(
            member.Participant,
            SerializeClient(new LeaveClientMessage()),
            CancellationToken.None);
        ServerMessage[] ownerMessages = await owner.FinishAsync();
        ServerMessage[] memberMessages = await member.FinishAsync();

        Assert.Equal("K7X2M9", Assert.Single(
            memberMessages.OfType<LeftServerMessage>()).RoomId);
        Assert.Contains(ownerMessages,
            message => message is ParticipantsServerMessage { Count: 1 });
        Assert.Null(member.Participant.CurrentRoom);
    }

    [Fact]
    public async Task Command_PartecipanteDentro_VieneInoltratoAlCommandHandler()
    {
        RoomManager manager = new();
        TestCommandHandler commands = new();
        MessageDispatcher dispatcher = CreateDispatcher(manager, commands);
        TestClient client = new();
        manager.Join("K7X2M9", "emy", ValidPublicKey(), client.Participant);
        CommandClientMessage command = new("users");

        await dispatcher.DispatchAsync(
            client.Participant,
            SerializeClient(command),
            CancellationToken.None);
        await client.FinishAsync();

        Assert.Same(client.Participant, commands.Participant);
        Assert.Equal(command, commands.Command);
    }

    [Fact]
    public async Task JsonMalformato_RestituisceMalformed()
    {
        RoomManager manager = new();
        MessageDispatcher dispatcher = CreateDispatcher(manager, new TestCommandHandler());
        TestClient client = new();

        await dispatcher.DispatchAsync(
            client.Participant,
            "{not-json",
            CancellationToken.None);
        ErrorServerMessage error = Assert.Single(
            (await client.FinishAsync()).OfType<ErrorServerMessage>());

        Assert.Equal(ErrorCodes.Malformed, error.Code);
    }

    private static MessageDispatcher CreateDispatcher(
        RoomManager manager,
        ICommandHandler commandHandler)
    {
        ProtocolMessageSender sender = new(manager);
        return new MessageDispatcher(
            manager,
            sender,
            commandHandler,
            new ActionRateLimiter(new FixedTimeProvider(FixedNow)),
            new JoinRateLimiter(new FixedTimeProvider(FixedNow)),
            new FixedTimeProvider(FixedNow));
    }

    private static string SerializeClient(ClientMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ClientMessage);

    private static string ValidPublicKey()
    {
        byte[] publicKey = new byte[KryptaLimits.PublicKeyBytes];
        publicKey[0] = 0x04;
        return Convert.ToBase64String(publicKey);
    }

    private static WrappedKey ValidWrappedKey()
    {
        byte[] epk = new byte[KryptaLimits.PublicKeyBytes];
        epk[0] = 0x04;
        return new WrappedKey(
            Convert.ToBase64String(epk),
            Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]),
            Convert.ToBase64String(new byte[KryptaLimits.WrappedKeyCiphertextBytes]));
    }

    private sealed class TestClient
    {
        private readonly FakeClientConnection _connection = new();
        private readonly Task _writer;

        public TestClient()
        {
            Participant = new Participant(_connection);
            _writer = Participant.RunWriterAsync(CancellationToken.None);
        }

        public Participant Participant { get; }

        public async Task<ServerMessage[]> FinishAsync()
        {
            Participant.CompleteOutgoing();
            await _writer;
            Participant.Dispose();

            return _connection.Sent
                .Select(json => JsonSerializer.Deserialize(
                    json,
                    ProtocolJsonContext.Default.ServerMessage)!)
                .ToArray();
        }
    }

    private sealed class TestCommandHandler : ICommandHandler
    {
        public Participant? Participant { get; private set; }

        public CommandClientMessage? Command { get; private set; }

        public ValueTask HandleAsync(
            Participant participant,
            CommandClientMessage command,
            CancellationToken cancellationToken)
        {
            Participant = participant;
            Command = command;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
