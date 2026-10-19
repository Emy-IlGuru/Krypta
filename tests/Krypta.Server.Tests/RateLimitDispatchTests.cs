using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests;

/// <summary>
/// Punto 1A.3: i limiti di frequenza visti dal dispatcher, cioè come li incontra un client.
/// </summary>
public sealed class RateLimitDispatchTests
{
    private const string Address = "192.168.1.31";

    [Fact]
    public async Task CommandRateLimit_QuartoComandoIn10s_Bloccato()
    {
        Harness harness = Harness.Create();
        TestParticipant author = harness.CreateRoom("emy");

        for (int index = 0; index < KryptaLimits.RateLimitCommands; index++)
        {
            await harness.Send(author, new CommandClientMessage(CommandNames.Users));
        }

        await harness.Send(author, new CommandClientMessage(CommandNames.Users));

        ErrorServerMessage error = Assert.Single(
            (await author.FinishAsync()).OfType<ErrorServerMessage>());
        Assert.Equal(ErrorCodes.CommandRateLimited, error.Code);
        Assert.True(error.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task CommandRateLimit_PassataLaFinestra_TornaAConsentire()
    {
        Harness harness = Harness.Create();
        TestParticipant author = harness.CreateRoom("emy");
        for (int index = 0; index < KryptaLimits.RateLimitCommands; index++)
        {
            await harness.Send(author, new CommandClientMessage(CommandNames.Users));
        }

        harness.Time.AdvanceSeconds(KryptaLimits.RateLimitCommandsWindowSeconds + 1);
        await harness.Send(author, new CommandClientMessage(CommandNames.Users));

        Assert.Empty((await author.FinishAsync()).OfType<ErrorServerMessage>());
    }

    [Fact]
    public async Task MessageRateLimit_UndicesimoIn5s_Bloccato()
    {
        Harness harness = Harness.Create();
        TestParticipant author = harness.CreateRoom("emy");

        for (int index = 1; index <= KryptaLimits.RateLimitMessages; index++)
        {
            await harness.Send(author, harness.Envelope(index));
        }

        await harness.Send(author, harness.Envelope(KryptaLimits.RateLimitMessages + 1));

        ServerMessage[] sent = await author.FinishAsync();
        Assert.Equal(
            ErrorCodes.RateLimited,
            Assert.Single(sent.OfType<ErrorServerMessage>()).Code);
        // I dieci passati sono comunque stati inoltrati: il limite scarta, non annulla.
        Assert.Equal(
            KryptaLimits.RateLimitMessages,
            sent.OfType<ChatServerMessage>().Count());
    }

    [Fact]
    public async Task JoinRateLimit_LeaveEJoinRipetuti_SullaStessaConnessione_Bloccati()
    {
        // Il test che chiude il buco di 04 §4.4: una connessione può fare leave e join
        // all'infinito, quindi contare solo il primo join lascerebbe scandire l'intero
        // spazio degli ID con un socket solo.
        Harness harness = Harness.Create();
        TestParticipant scanner = new(Address);

        for (int index = 0; index < KryptaLimits.JoinRateLimitAttempts; index++)
        {
            await harness.Send(scanner, Join($"ROOM{index:D2}"));
            await harness.Send(scanner, new LeaveClientMessage());
        }

        await harness.Send(scanner, Join("ROOMXX"));

        ErrorServerMessage error = Assert.Single(
            (await scanner.FinishAsync()).OfType<ErrorServerMessage>());
        Assert.Equal(ErrorCodes.JoinRateLimited, error.Code);
        Assert.Equal(KryptaLimits.JoinRateLimitBlockSeconds, error.RetryAfterSeconds);
    }

    [Fact]
    public async Task JoinRateLimit_RiconnessioneStessoIp_NonAzzeraIlContatore()
    {
        // La chiave è l'indirizzo, non il socket: riaprire la connessione non aiuta.
        Harness harness = Harness.Create();

        for (int index = 0; index < KryptaLimits.JoinRateLimitAttempts; index++)
        {
            TestParticipant attempt = new(Address);
            await harness.Send(attempt, Join($"ROOM{index:D2}"));
            await attempt.FinishAsync();
        }

        TestParticipant reconnected = new(Address);
        await harness.Send(reconnected, Join("ROOMXX"));

        Assert.Equal(
            ErrorCodes.JoinRateLimited,
            Assert.Single(
                (await reconnected.FinishAsync()).OfType<ErrorServerMessage>()).Code);
    }

    [Fact]
    public async Task JoinRateLimit_NonTocccaGliAltriMessaggi()
    {
        // Superato il limite sui join, chi è già dentro continua a parlare: il blocco
        // riguarda l'ingresso, non la connessione, che infatti non viene chiusa.
        Harness harness = Harness.Create();
        TestParticipant author = harness.CreateRoom("emy", Address);
        for (int index = 0; index < KryptaLimits.JoinRateLimitAttempts + 2; index++)
        {
            TestParticipant noise = new(Address);
            await harness.Send(noise, Join("ROOMZZ"));
            await noise.FinishAsync();
        }

        await harness.Send(author, harness.Envelope(1));

        ServerMessage[] sent = await author.FinishAsync();
        Assert.Single(sent.OfType<ChatServerMessage>());
        Assert.Empty(sent.OfType<ErrorServerMessage>());
    }

    private static JoinClientMessage Join(string roomId) =>
        new(roomId, "scanner", ProtocolFixture.PublicKey());

    private sealed class Harness
    {
        private readonly MessageDispatcher _dispatcher;
        private readonly RoomManager _manager;

        private Harness(
            MessageDispatcher dispatcher,
            RoomManager manager,
            MovableTimeProvider time)
        {
            _dispatcher = dispatcher;
            _manager = manager;
            Time = time;
        }

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

        public TestParticipant CreateRoom(string nickname, string? remoteAddress = null)
        {
            TestParticipant owner = new(remoteAddress);
            _manager.Join(
                "K7X2M9",
                nickname,
                ProtocolFixture.PublicKey(),
                owner.Participant);
            return owner;
        }

        public ChatClientMessage Envelope(int seq) =>
            new(
                seq,
                1,
                Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]),
                Convert.ToBase64String(new byte[KryptaLimits.GcmTagBytes + 4]));

        public ValueTask Send(TestParticipant participant, ClientMessage message) =>
            _dispatcher.DispatchAsync(
                participant.Participant,
                ProtocolFixture.SerializeClient(message),
                CancellationToken.None);
    }
}
