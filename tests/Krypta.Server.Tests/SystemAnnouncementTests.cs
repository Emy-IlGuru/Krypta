using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests;

/// <summary>
/// Punto 1A.3: i messaggi <c>system</c> a ogni evento della stanza.
/// </summary>
public sealed class SystemAnnouncementTests
{
    [Fact]
    public async Task Join_StanzaNuova_AnnunciaRoomCreated()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant creator = new();

        await dispatcher.DispatchAsync(
            creator.Participant,
            ProtocolFixture.SerializeClient(
                new JoinClientMessage("K7X2M9", "emy", ProtocolFixture.PublicKey())),
            CancellationToken.None);

        SystemServerMessage announcement = Assert.Single(
            (await creator.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.RoomCreated, announcement.Kind);
        Assert.Equal("emy ha creato la stanza", announcement.Text);
        Assert.Equal(ProtocolFixture.Now, announcement.SentAt);
    }

    [Fact]
    public async Task Join_StanzaEsistente_AnnunciaJoinRequestedAiSoliPartecipanti()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant owner = new();
        TestParticipant requester = new();
        manager.Join("K7X2M9", "emy", ProtocolFixture.PublicKey(), owner.Participant);

        await dispatcher.DispatchAsync(
            requester.Participant,
            ProtocolFixture.SerializeClient(
                new JoinClientMessage("K7X2M9", "sara", ProtocolFixture.PublicKey())),
            CancellationToken.None);

        SystemServerMessage announcement = Assert.Single(
            (await owner.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.JoinRequested, announcement.Kind);
        Assert.Equal("sara chiede di entrare nella stanza", announcement.Text);

        // Chi è in attesa non riceve niente della stanza: nemmeno le righe di sistema,
        // che direbbero quante persone ci sono e come si chiamano.
        Assert.Empty((await requester.FinishAsync()).Systems());
    }

    [Fact]
    public async Task Approve_AnnunciaUserJoinedConIlNomeDiChiHaAccettato()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant owner = new();
        TestParticipant requester = new();
        manager.Join("K7X2M9", "emy", ProtocolFixture.PublicKey(), owner.Participant);
        JoinResult pending = manager.Join(
            "K7X2M9",
            "sara",
            ProtocolFixture.PublicKey(),
            requester.Participant);

        await dispatcher.DispatchAsync(
            owner.Participant,
            ProtocolFixture.SerializeClient(new ApproveClientMessage(
                pending.RequestId!.Value,
                ProtocolFixture.WrappedKey(),
                1)),
            CancellationToken.None);

        SystemServerMessage announcement = Assert.Single(
            (await owner.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.UserJoined, announcement.Kind);
        Assert.Equal("sara si è unito alla stanza, accettato da emy", announcement.Text);
    }

    [Fact]
    public async Task Approve_OrdineVincolante_WaitingPoiSystemPoiParticipants()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant owner = new();
        TestParticipant requester = new();
        manager.Join("K7X2M9", "emy", ProtocolFixture.PublicKey(), owner.Participant);
        JoinResult pending = manager.Join(
            "K7X2M9",
            "sara",
            ProtocolFixture.PublicKey(),
            requester.Participant);

        await dispatcher.DispatchAsync(
            owner.Participant,
            ProtocolFixture.SerializeClient(new ApproveClientMessage(
                pending.RequestId!.Value,
                ProtocolFixture.WrappedKey(),
                1)),
            CancellationToken.None);

        Assert.Equal(
            ["waiting", $"system:{SystemKinds.UserJoined}", "participants", "roster"],
            (await owner.FinishAsync()).Kinds());
    }

    [Fact]
    public async Task Reject_AnnunciaChiHaRifiutatoEChi()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant owner = new();
        TestParticipant requester = new();
        manager.Join("K7X2M9", "luca", ProtocolFixture.PublicKey(), owner.Participant);
        JoinResult pending = manager.Join(
            "K7X2M9",
            "sara",
            ProtocolFixture.PublicKey(),
            requester.Participant);

        await dispatcher.DispatchAsync(
            owner.Participant,
            ProtocolFixture.SerializeClient(
                new RejectClientMessage(pending.RequestId!.Value)),
            CancellationToken.None);

        SystemServerMessage announcement = Assert.Single(
            (await owner.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.JoinRejected, announcement.Kind);
        Assert.Equal("luca ha rifiutato sara", announcement.Text);
    }

    [Fact]
    public async Task Leave_AnnunciaUserLeftAiRimasti()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant owner = new();
        TestParticipant member = new();
        manager.Join("K7X2M9", "emy", ProtocolFixture.PublicKey(), owner.Participant);
        Admit(manager, owner, member, "luca");

        await dispatcher.DispatchAsync(
            member.Participant,
            ProtocolFixture.SerializeClient(new LeaveClientMessage()),
            CancellationToken.None);

        SystemServerMessage announcement = Assert.Single(
            (await owner.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.UserLeft, announcement.Kind);
        Assert.Equal("luca è uscito dalla stanza", announcement.Text);
    }

    [Fact]
    public async Task Disconnessione_Brusca_AnnunciaUgualmenteUserLeft()
    {
        RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
        MessageDispatcher dispatcher = CreateDispatcher(manager);
        TestParticipant owner = new();
        TestParticipant member = new();
        manager.Join("K7X2M9", "emy", ProtocolFixture.PublicKey(), owner.Participant);
        Admit(manager, owner, member, "luca");

        // Nessun "leave": la connessione sparisce e basta.
        dispatcher.Disconnect(member.Participant);

        SystemServerMessage announcement = Assert.Single(
            (await owner.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.UserLeft, announcement.Kind);
        Assert.Equal("luca è uscito dalla stanza", announcement.Text);
    }

    /// <summary>Fa entrare davvero un partecipante: richiesta e poi approvazione.</summary>
    private static void Admit(
        RoomManager manager,
        TestParticipant approver,
        TestParticipant candidate,
        string nickname)
    {
        JoinResult pending = manager.Join(
            "K7X2M9",
            nickname,
            ProtocolFixture.PublicKey(),
            candidate.Participant);
        manager.Approve(
            approver.Participant,
            pending.RequestId!.Value,
            ProtocolFixture.WrappedKey(),
            1);
    }

    private static MessageDispatcher CreateDispatcher(RoomManager manager)
    {
        ProtocolMessageSender sender = new(manager);
        StoppedTimeProvider time = new(ProtocolFixture.Now);
        return new MessageDispatcher(
            manager,
            sender,
            new CommandHandler(manager, sender, time),
            new ActionRateLimiter(time),
            new JoinRateLimiter(time),
            time);
    }
}
