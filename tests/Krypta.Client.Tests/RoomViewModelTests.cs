using System.Reactive.Linq;
using System.Text.Json;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using Krypta.Client.Tests.TestDoubles;
using Krypta.Client.ViewModels;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using ReactiveUI;

namespace Krypta.Client.Tests;

/// <summary>
/// Punto 1A.5, schermata [3]: ID della stanza, pulsante «Genera», messaggio di richiesta
/// e conto alla rovescia sul limite di ingressi ([05 §4](../../docs/05-client-desktop.md)).
/// </summary>
public sealed class RoomViewModelTests
{
    [Theory]
    [InlineData("k7x2m9", "K7X2M9")]
    [InlineData("  k7x 2m9  ", "K7X2M9")]
    [InlineData("k7-x2/m9!!", "K7X2M9")]
    [InlineData("K7X2M9EXTRA", "K7X2M9")]
    [InlineData("àèì", "")]
    public async Task RoomId_ConvertiInMaiuscoloEScartaIlResto(string typed, string expected)
    {
        await using Fixture fixture = new();

        fixture.ViewModel.RoomId = typed;

        Assert.Equal(expected, fixture.ViewModel.RoomId);
    }

    [Fact]
    public async Task RoomId_SoloConSeiCaratteri_AbilitaEntra()
    {
        await using Fixture fixture = new();

        fixture.ViewModel.RoomId = "K7X2M";
        Assert.False(fixture.ViewModel.CanJoin);

        fixture.ViewModel.RoomId = "K7X2M9";
        Assert.True(fixture.ViewModel.CanJoin);
    }

    [Fact]
    public async Task GenerateId_RiempieIlCampoConUnIdChePassaLaValidazione()
    {
        await using Fixture fixture = new();

        await fixture.ViewModel.GenerateIdCommand.Execute();

        Assert.Equal(KryptaLimits.RoomIdLength, fixture.ViewModel.RoomId.Length);
        Assert.True(fixture.ViewModel.CanJoin);
    }

    [Fact]
    public async Task JoinMessage_EPrecompilatoEContaICaratteri()
    {
        await using Fixture fixture = new();

        Assert.Equal(RoomViewModel.DefaultJoinMessage, fixture.ViewModel.JoinMessage);
        Assert.Equal(
            RoomViewModel.DefaultJoinMessage.Length,
            fixture.ViewModel.JoinMessageLength);
        Assert.False(fixture.ViewModel.IsJoinMessageAtLimit);
    }

    [Fact]
    public async Task JoinMessage_RestaSuUnaRigaSolaEDentroIlLimite()
    {
        await using Fixture fixture = new();

        fixture.ViewModel.JoinMessage = "prima\r\nseconda\triga";
        Assert.Equal("primasecondariga", fixture.ViewModel.JoinMessage);

        fixture.ViewModel.JoinMessage = new string('a', 250);
        Assert.Equal(KryptaLimits.JoinMessageMaxLength, fixture.ViewModel.JoinMessageLength);
        Assert.True(fixture.ViewModel.IsJoinMessageAtLimit);
    }

    [Fact]
    public async Task Join_StanzaNuova_InviaLaRichiestaERiportaLaCreazione()
    {
        await using Fixture fixture = new();
        fixture.ViewModel.RoomId = "K7X2M9";

        await fixture.JoinAndAnswerAsync(new JoinedServerMessage(
            "K7X2M9", "RM-K7X2M9", "emy", 1, Created: true, 20, 1));

        JoinClientMessage sent = fixture.SentJoin();
        Assert.Equal("K7X2M9", sent.RoomId);
        Assert.Equal("emy", sent.Nickname);
        Assert.Equal(RoomViewModel.DefaultJoinMessage, sent.JoinMessage);
        Assert.Equal(JoinAttempt.Created, fixture.ViewModel.Outcome);
        Assert.Equal("K7X2M9", fixture.ViewModel.JoinedRoomId);
        Assert.True(fixture.ViewModel.HasStatus);
    }

    [Fact]
    public async Task Join_MessaggioSvuotato_NonVieneInviato()
    {
        // Svuotare il campo è legittimo (RF-C-A11): non è un messaggio vuoto, è assente.
        await using Fixture fixture = new();
        fixture.ViewModel.RoomId = "K7X2M9";
        fixture.ViewModel.JoinMessage = "   ";

        await fixture.JoinAndAnswerAsync(new JoinedServerMessage(
            "K7X2M9", "RM-K7X2M9", "emy", 1, Created: true, 20, 1));

        Assert.Null(fixture.SentJoin().JoinMessage);
    }

    [Fact]
    public async Task Join_StanzaEsistente_RiportaLaSalaDAttesa()
    {
        await using Fixture fixture = new();
        fixture.ViewModel.RoomId = "K7X2M9";

        await fixture.JoinAndAnswerAsync(new PendingServerMessage("K7X2M9", 120));

        Assert.Equal(JoinAttempt.Pending, fixture.ViewModel.Outcome);
        Assert.Contains("120", fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(fixture.ViewModel.CanJoin);
    }

    [Fact]
    public async Task Join_StanzaPiena_DiceIlMotivoEConsenteDiRiprovare()
    {
        await using Fixture fixture = new();
        fixture.ViewModel.RoomId = "K7X2M9";

        await fixture.JoinAndAnswerAsync(
            new ErrorServerMessage(ErrorCodes.RoomFull, "piena"));

        Assert.Equal(JoinAttempt.None, fixture.ViewModel.Outcome);
        Assert.Equal("La stanza è piena (20/20).", fixture.ViewModel.ErrorMessage);
        Assert.True(fixture.ViewModel.CanJoin);
    }

    [Fact]
    public async Task Join_LimiteDiIngressi_BloccaEContaAllaRovescia()
    {
        await using Fixture fixture = new();
        fixture.ViewModel.RoomId = "K7X2M9";

        await fixture.JoinAndAnswerAsync(new ErrorServerMessage(
            ErrorCodes.JoinRateLimited, "troppi tentativi", RetryAfterSeconds: 3));

        Assert.Equal(3, fixture.ViewModel.RetryAfterSeconds);
        Assert.False(fixture.ViewModel.CanJoin);
        Assert.Contains("3 secondi", fixture.ViewModel.ErrorMessage, StringComparison.Ordinal);

        fixture.ViewModel.CountdownTick();
        fixture.ViewModel.CountdownTick();
        Assert.Contains("1 secondo.", fixture.ViewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(fixture.ViewModel.CanJoin);

        fixture.ViewModel.CountdownTick();
        Assert.Equal(0, fixture.ViewModel.RetryAfterSeconds);
        Assert.False(fixture.ViewModel.HasError);
        Assert.True(fixture.ViewModel.CanJoin);
    }

    [Fact]
    public async Task Join_GiaRiuscito_NonSiRipete()
    {
        // Un secondo `join` sulla stessa connessione prenderebbe ALREADY_IN_ROOM.
        await using Fixture fixture = new();
        fixture.ViewModel.RoomId = "K7X2M9";
        await fixture.JoinAndAnswerAsync(new JoinedServerMessage(
            "K7X2M9", "RM-K7X2M9", "emy", 1, Created: true, 20, 1));

        Assert.False(fixture.ViewModel.CanJoin);
        Assert.False(fixture.ViewModel.CanEdit);

        await fixture.ViewModel.JoinAsync();

        Assert.Single(fixture.Transport.SentMessages);
    }

    [Fact]
    public async Task ChangeNickname_TornaAllaSchermataPrecedente()
    {
        await using Fixture fixture = new();
        await fixture.Screen.Router.Navigate.Execute(fixture.ViewModel);

        await fixture.ViewModel.ChangeNicknameCommand.Execute();

        Assert.Empty(fixture.Screen.Router.NavigationStack);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture()
        {
            Transport = new FakeWebSocketTransport();
            Connection = new KryptaConnection(() => Transport, Dispatcher, TimeProvider.System);
            Connection.ConnectAsync("127.0.0.1", 5000, useTls: false)
                .GetAwaiter()
                .GetResult();

            ViewModel = new RoomViewModel(
                Screen,
                Connection,
                new SessionSettings(
                    "127.0.0.1",
                    5000,
                    UseTls: false,
                    Remember: false,
                    new ConfigLoadResult(ClientConfig.Default)),
                "emy",
                // Nessun timer vero: il conto alla rovescia lo guidano i test.
                static () => Observable.Never<long>());
        }

        public TestScreen Screen { get; } = new();

        public QueuedUiDispatcher Dispatcher { get; } = new();

        public FakeWebSocketTransport Transport { get; }

        public KryptaConnection Connection { get; }

        public RoomViewModel ViewModel { get; }

        public async Task JoinAndAnswerAsync(ServerMessage answer)
        {
            Task join = ViewModel.JoinAsync();
            await WaitUntilAsync(() => !Transport.SentMessages.IsEmpty);

            Transport.QueueText(JsonSerializer.Serialize(
                answer,
                ProtocolJsonContext.Default.ServerMessage));
            await WaitUntilAsync(() => Dispatcher.PendingCount > 0);
            Dispatcher.Drain();

            await join;
        }

        public JoinClientMessage SentJoin()
        {
            Assert.True(Transport.SentMessages.TryPeek(out string? json));
            return Assert.IsType<JoinClientMessage>(JsonSerializer.Deserialize(
                json!,
                ProtocolJsonContext.Default.ClientMessage));
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            while (!condition())
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }
}
