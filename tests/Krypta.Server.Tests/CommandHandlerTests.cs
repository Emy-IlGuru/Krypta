using Krypta.Server.Messaging;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests;

/// <summary>
/// Punto 1A.3: /NameRoom, /Users e /ClearRoom.
/// </summary>
public sealed class CommandHandlerTests
{
    [Theory]
    [InlineData("Coordinamento", "Coordinamento")]
    [InlineData("Coordinamento Team", "Coordinamento Team")]
    [InlineData("  Team   A  ", "Team A")]
    [InlineData("Riunione 2026", "Riunione 2026")]
    public async Task NameRoom_Valido_NormalizzaRinominaEAnnuncia(
        string argument,
        string expected)
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant author);

        await fixture.Run(author, CommandNames.NameRoom, argument);

        Assert.Equal(expected, fixture.Room.Name);
        SystemServerMessage announcement = Assert.Single(
            (await author.FinishAsync()).Systems());
        Assert.Equal(SystemKinds.RoomRenamed, announcement.Kind);
        Assert.Equal(expected, announcement.RoomName);
        Assert.Equal($"emy ha rinominato la stanza in «{expected}»", announcement.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Team-A!")]
    [InlineData("Riunione: lunedì")]
    [InlineData("Coordinamento generale del team di progetto")]
    public async Task NameRoom_NonValido_RestituisceInvalidCommandArgSenzaRinominare(
        string? argument)
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant author);
        string original = fixture.Room.Name;

        await fixture.Run(author, CommandNames.NameRoom, argument);

        Assert.Equal(original, fixture.Room.Name);
        ServerMessage[] sent = await author.FinishAsync();
        Assert.Equal(
            ErrorCodes.InvalidCommandArg,
            Assert.Single(sent.OfType<ErrorServerMessage>()).Code);
        Assert.Empty(sent.Systems());
    }

    [Fact]
    public async Task NameRoom_LunghezzaMisurataDopoLaNormalizzazione()
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant author);

        // 25 caratteri solo dopo aver ridotto le sequenze di spazi: prima ne conta 31.
        await fixture.Run(author, CommandNames.NameRoom, "Coordinamento     del     team");

        Assert.Equal("Coordinamento del team", fixture.Room.Name);
        Assert.Empty((await author.FinishAsync()).OfType<ErrorServerMessage>());
    }

    [Fact]
    public async Task Users_ElencaInOrdineDiIngressoEDArrivaATutti()
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant owner);
        TestParticipant second = fixture.Admit(owner, "luca");
        TestParticipant third = fixture.Admit(owner, "sara");

        await fixture.Run(third, CommandNames.Users);

        foreach (TestParticipant everyone in new[] { owner, second, third })
        {
            SystemServerMessage list = Assert.Single(
                (await everyone.FinishAsync()).Systems()
                    .Where(item => item.Kind == SystemKinds.UserList));
            Assert.Equal(["emy", "luca", "sara"], list.Users);
            Assert.Equal("Partecipanti (3/20): emy, luca, sara", list.Text);
        }
    }

    [Fact]
    public async Task ClearRoom_InviaClearPrimaDelSystemEDiceChiEStato()
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant owner);
        TestParticipant other = fixture.Admit(owner, "luca");

        await fixture.Run(other, CommandNames.ClearRoom);

        // L'ordine è vincolante: al contrario, la riga che spiega chi ha svuotato
        // la stanza verrebbe cancellata subito dopo essere apparsa.
        string[] received = (await owner.FinishAsync()).Kinds();
        int clear = Array.IndexOf(received, "clear");
        int announcement = Array.IndexOf(received, $"system:{SystemKinds.RoomCleared}");
        Assert.True(clear >= 0 && announcement >= 0);
        Assert.True(clear < announcement);

        SystemServerMessage cleared = Assert.Single(
            (await other.FinishAsync()).Systems()
                .Where(item => item.Kind == SystemKinds.RoomCleared));
        Assert.Equal("luca ha svuotato la stanza", cleared.Text);
    }

    [Theory]
    [InlineData("pippo")]
    [InlineData("help")]
    [InlineData("generatekey")]
    public async Task ComandoSconosciuto_RispondeSoloAlMittente(string name)
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant owner);
        TestParticipant other = fixture.Admit(owner, "luca");

        await fixture.Run(other, name);

        Assert.Equal(
            ErrorCodes.UnknownCommand,
            Assert.Single((await other.FinishAsync()).OfType<ErrorServerMessage>()).Code);
        Assert.Empty((await owner.FinishAsync()).OfType<ErrorServerMessage>());
    }

    [Theory]
    [InlineData("NameRoom")]
    [InlineData("NAMEROOM")]
    [InlineData("nameroom")]
    public async Task NomeDelComando_InsensibileAMaiuscole(string name)
    {
        Fixture fixture = Fixture.WithRoom(out TestParticipant author);

        await fixture.Run(author, name, "Coordinamento");

        Assert.Equal("Coordinamento", fixture.Room.Name);
    }

    private sealed class Fixture
    {
        private readonly RoomManager _manager;
        private readonly CommandHandler _handler;

        private Fixture(RoomManager manager, CommandHandler handler, Room room)
        {
            _manager = manager;
            _handler = handler;
            Room = room;
        }

        public Room Room { get; }

        public static Fixture WithRoom(out TestParticipant owner)
        {
            RoomManager manager = new(new StoppedTimeProvider(ProtocolFixture.Now));
            ProtocolMessageSender sender = new(manager);
            StoppedTimeProvider time = new(ProtocolFixture.Now);
            owner = new TestParticipant();
            manager.Join("K7X2M9", "emy", ProtocolFixture.PublicKey(), owner.Participant);
            manager.TryGetRoom("K7X2M9", out Room? room);
            return new Fixture(
                manager,
                new CommandHandler(manager, sender, time),
                room!);
        }

        public TestParticipant Admit(TestParticipant approver, string nickname)
        {
            TestParticipant candidate = new();
            JoinResult pending = _manager.Join(
                "K7X2M9",
                nickname,
                ProtocolFixture.PublicKey(),
                candidate.Participant);
            _manager.Approve(
                approver.Participant,
                pending.RequestId!.Value,
                ProtocolFixture.WrappedKey(),
                1);
            return candidate;
        }

        public ValueTask Run(TestParticipant sender, string name, string? argument = null) =>
            _handler.HandleAsync(
                sender.Participant,
                new CommandClientMessage(name, argument),
                CancellationToken.None);
    }
}
