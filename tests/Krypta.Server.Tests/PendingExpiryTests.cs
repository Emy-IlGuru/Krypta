using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Krypta.Server.Tests;

/// <summary>
/// Punto 1A.3: la scadenza a 120 secondi delle richieste in sala d'attesa (RF-S-A11).
/// Il server annuncia il tempo nel `pending`, quindi deve anche mantenerlo.
/// </summary>
public sealed class PendingExpiryTests
{
    [Fact]
    public async Task Timeout_DopoDueMinuti_ChiudeLaRichiesta()
    {
        Harness harness = Harness.Create();
        harness.CreateRoom("emy");
        TestParticipant knocker = await harness.Knock("sara");

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds);
        Assert.Equal(1, harness.Expire());

        RejectedServerMessage rejected = Assert.Single(
            (await knocker.FinishAsync()).OfType<RejectedServerMessage>());
        Assert.Equal(RejectionReasons.Timeout, rejected.Reason);
        Assert.Equal("K7X2M9", rejected.RoomId);
    }

    [Fact]
    public async Task Timeout_UnSecondoPrima_NonChiudeNulla()
    {
        Harness harness = Harness.Create();
        harness.CreateRoom("emy");
        TestParticipant knocker = await harness.Knock("sara");

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds - 1);

        Assert.Equal(0, harness.Expire());
        Assert.Empty((await knocker.FinishAsync()).OfType<RejectedServerMessage>());
    }

    [Fact]
    public async Task Timeout_RichiestaScaduta_AggiornaLaCodaSenzaRigaDiSistema()
    {
        // Nessuno ha deciso: `join_rejected` direbbe chi è stato (RF-S-A07), e non c'è.
        Harness harness = Harness.Create();
        TestParticipant owner = harness.CreateRoom("emy");
        await harness.Knock("sara");

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds);
        harness.Expire();

        ServerMessage[] sent = await owner.FinishAsync();
        Assert.Equal(
            ["waiting", $"system:{SystemKinds.JoinRequested}", "waiting"],
            sent.Kinds());
        Assert.Empty(sent.OfType<WaitingServerMessage>().Last().Requests);
    }

    [Fact]
    public async Task Timeout_RichiestaGiaApprovata_NonProduceNulla()
    {
        // I10: una richiesta si evade una volta sola, e la scadenza non fa eccezione.
        Harness harness = Harness.Create();
        TestParticipant owner = harness.CreateRoom("emy");
        TestParticipant knocker = await harness.Knock("sara");
        harness.Approve(owner);

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds * 2);

        Assert.Equal(0, harness.Expire());
        Assert.Empty((await knocker.FinishAsync()).OfType<RejectedServerMessage>());
    }

    [Fact]
    public async Task Timeout_DopoLaScadenza_SiPuoBussareDiNuovo()
    {
        // Scaduta la richiesta, il richiedente non è più "in attesa": senza l'uscita
        // dallo stato pendente riceverebbe ALREADY_PENDING per sempre.
        Harness harness = Harness.Create();
        harness.CreateRoom("emy");
        TestParticipant knocker = await harness.Knock("sara");
        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds);
        harness.Expire();

        await harness.Send(knocker, Harness.Join("sara"));

        ServerMessage[] sent = await knocker.FinishAsync();
        Assert.Empty(sent.OfType<ErrorServerMessage>());
        Assert.Equal(2, sent.OfType<PendingServerMessage>().Count());
    }

    [Fact]
    public async Task Timeout_TreRichiesteInsieme_UnaSolaCodaAggiornata()
    {
        Harness harness = Harness.Create();
        TestParticipant owner = harness.CreateRoom("emy");
        await harness.Knock("sara");
        await harness.Knock("luca");
        await harness.Knock("gio");

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds);
        Assert.Equal(3, harness.Expire());

        ServerMessage[] sent = await owner.FinishAsync();
        // Tre richieste in arrivo, tre code; alla scadenza una sola, non tre.
        Assert.Equal(4, sent.OfType<WaitingServerMessage>().Count());
        Assert.Empty(sent.OfType<WaitingServerMessage>().Last().Requests);
    }

    [Fact]
    public async Task Timeout_StanzeDiverse_ScadeSoloQuellaDovuta()
    {
        Harness harness = Harness.Create();
        harness.CreateRoom("emy");
        TestParticipant vecchia = await harness.Knock("sara");

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds - 10);
        harness.CreateRoom("luca", "AB12CD");
        TestParticipant recente = await harness.Knock("gio", "AB12CD");
        harness.Time.AdvanceSeconds(10);

        Assert.Equal(1, harness.Expire());
        Assert.Single((await vecchia.FinishAsync()).OfType<RejectedServerMessage>());
        Assert.Empty((await recente.FinishAsync()).OfType<RejectedServerMessage>());
    }

    [Fact]
    public async Task Timeout_ServizioPeriodico_ScadeSenzaDispatcherEsterno()
    {
        // La stessa cosa passando dal BackgroundService registrato in Program.cs.
        Harness harness = Harness.Create();
        harness.CreateRoom("emy");
        TestParticipant knocker = await harness.Knock("sara");
        PendingExpiryService service = new(
            harness.Dispatcher,
            harness.Time,
            NullLogger<PendingExpiryService>.Instance);

        harness.Time.AdvanceSeconds(KryptaLimits.JoinApprovalTimeoutSeconds);
        service.RunOnce();

        Assert.Equal(
            RejectionReasons.Timeout,
            Assert.Single(
                (await knocker.FinishAsync()).OfType<RejectedServerMessage>()).Reason);
    }

    private sealed class Harness
    {
        private readonly RoomManager _manager;

        private Harness(
            MessageDispatcher dispatcher,
            RoomManager manager,
            MovableTimeProvider time)
        {
            Dispatcher = dispatcher;
            _manager = manager;
            Time = time;
        }

        public MessageDispatcher Dispatcher { get; }

        public MovableTimeProvider Time { get; }

        public static Harness Create()
        {
            MovableTimeProvider time = new(ProtocolFixture.Now);
            RoomManager manager = new(time);
            ProtocolMessageSender sender = new(manager);
            MessageDispatcher dispatcher = new(
                manager,
                sender,
                new CommandHandler(manager, sender, time),
                new ActionRateLimiter(time),
                new JoinRateLimiter(time),
                time);
            return new Harness(dispatcher, manager, time);
        }

        public static JoinClientMessage Join(string nickname, string roomId = "K7X2M9") =>
            new(roomId, nickname, ProtocolFixture.PublicKey());

        public TestParticipant CreateRoom(string nickname, string roomId = "K7X2M9")
        {
            TestParticipant owner = new();
            _manager.Join(roomId, nickname, ProtocolFixture.PublicKey(), owner.Participant);
            return owner;
        }

        public async Task<TestParticipant> Knock(
            string nickname,
            string roomId = "K7X2M9")
        {
            TestParticipant knocker = new();
            await Send(knocker, Join(nickname, roomId));
            return knocker;
        }

        public void Approve(TestParticipant approver, string roomId = "K7X2M9")
        {
            _manager.TryGetRoom(roomId, out Room? room);
            _manager.Approve(
                approver.Participant,
                room!.Pending[0].RequestId,
                ProtocolFixture.WrappedKey(),
                room.KeyEpoch);
        }

        public int Expire() => Dispatcher.ExpirePendingRequests();

        public ValueTask Send(TestParticipant participant, ClientMessage message) =>
            Dispatcher.DispatchAsync(
                participant.Participant,
                ProtocolFixture.SerializeClient(message),
                CancellationToken.None);
    }
}
