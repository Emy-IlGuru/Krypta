using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Krypta.Client.Chat;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using Krypta.Client.Tests.TestDoubles;
using Krypta.Client.ViewModels;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using ReactiveUI;

namespace Krypta.Client.Tests;

/// <summary>
/// Punto 1A.5, schermata [4]: le tre fasce, il formato a due righe e i messaggi di
/// sistema ([05 §5](../../docs/05-client-desktop.md)).
/// </summary>
public sealed class ChatViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Init_PrendeNomeIdEConteggioDalJoined()
    {
        await using Fixture fixture = new();

        Assert.Equal("K7X2M9", fixture.ViewModel.RoomId);
        Assert.Equal("RM-K7X2M9", fixture.ViewModel.RoomName);
        Assert.Equal("emy", fixture.ViewModel.MyNickname);
        Assert.Equal("1/20", fixture.ViewModel.ParticipantLabel);
        Assert.Empty(fixture.ViewModel.Messages);
    }

    [Fact]
    public async Task Messaggio_InArrivo_DiventaUnaRigaDelMittente()
    {
        await using Fixture fixture = new();

        await fixture.ReceiveAsync(new ChatServerMessage(
            "luca", 1, 1, ClearEnvelope.ClearNonce, ClearEnvelope.Wrap("ciao a tutti"), Now));

        ChatLine line = Assert.Single(fixture.ViewModel.Messages);
        Assert.Equal("luca", line.Sender);
        Assert.Equal("ciao a tutti", line.Text);
        Assert.False(line.IsDimmed);
        Assert.IsType<UserChatEntry>(line.Entry);
    }

    [Fact]
    public async Task Messaggio_ConInvolucroNonNostro_DiventaLaRigaNonVerificato()
    {
        // In 1A non c'è ancora il tag GCM, ma il bivio è già quello della 1B: quello che
        // non si sa leggere non si mostra come testo, e non si scarta in silenzio.
        await using Fixture fixture = new();
        string nonce = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(KryptaLimits.NonceBytes));

        await fixture.ReceiveAsync(new ChatServerMessage(
            "luca", 1, 1, nonce, ClearEnvelope.Wrap("testo che sembra leggibile"), Now));

        ChatLine line = Assert.Single(fixture.ViewModel.Messages);
        Assert.Equal("KRYPTA", line.Sender);
        Assert.Contains("luca", line.Text, StringComparison.Ordinal);
        Assert.Contains("non è stato verificato", line.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sembra leggibile", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_DiventaUnaRigaAttenuataConMittenteStanza()
    {
        await using Fixture fixture = new();

        await fixture.ReceiveAsync(new SystemServerMessage(
            SystemKinds.UserJoined, "luca si è unito alla stanza", Now));

        ChatLine line = Assert.Single(fixture.ViewModel.Messages);
        Assert.Equal("STANZA", line.Sender);
        Assert.Equal("luca si è unito alla stanza", line.Text);
        Assert.True(line.IsDimmed);
    }

    [Fact]
    public async Task RoomRenamed_AggiornaLIntestazioneOltreAllaRiga()
    {
        await using Fixture fixture = new();

        await fixture.ReceiveAsync(new SystemServerMessage(
            SystemKinds.RoomRenamed,
            "emy ha rinominato la stanza in «Coordinamento»",
            Now,
            RoomName: "Coordinamento"));

        Assert.Equal("Coordinamento", fixture.ViewModel.RoomName);
        Assert.Single(fixture.ViewModel.Messages);
    }

    [Fact]
    public async Task Clear_SvuotaLaListaLocale()
    {
        await using Fixture fixture = new();
        await fixture.ReceiveAsync(new SystemServerMessage(
            SystemKinds.RoomCreated, "emy ha creato la stanza", Now));

        await fixture.ReceiveAsync(new ClearServerMessage());

        Assert.Empty(fixture.ViewModel.Messages);
    }

    [Fact]
    public async Task Participants_AggiornaIlContatore()
    {
        await using Fixture fixture = new();

        await fixture.ReceiveAsync(new ParticipantsServerMessage(3));

        Assert.Equal("3/20", fixture.ViewModel.ParticipantLabel);
    }

    [Fact]
    public async Task Invio_SvuotaSubitoLaCasellaMaNonScriveInChat()
    {
        // Il messaggio compare solo quando torna dal server (RF-C-19): senza l'eco non si
        // saprebbe se è davvero partito.
        await using Fixture fixture = new();
        fixture.ViewModel.Draft = "  ciao  ";

        await fixture.ViewModel.SendAsync();

        Assert.Equal(string.Empty, fixture.ViewModel.Draft);
        Assert.Empty(fixture.ViewModel.Messages);

        ChatClientMessage sent = fixture.SentMessages().Single();
        Assert.Equal(1, sent.Seq);
        Assert.Equal(1, sent.KeyEpoch);
        Assert.Equal(ClearEnvelope.ClearNonce, sent.Nonce);
        Assert.True(ClearEnvelope.TryUnwrap(sent.Nonce, sent.Ciphertext, out string text));
        Assert.Equal("ciao", text);
    }

    [Fact]
    public async Task Invio_MessaggiSuccessivi_FannoCrescereIlSeq()
    {
        await using Fixture fixture = new();

        fixture.ViewModel.Draft = "primo";
        await fixture.ViewModel.SendAsync();
        fixture.ViewModel.Draft = "secondo";
        await fixture.ViewModel.SendAsync();

        Assert.Equal([1, 2], fixture.SentMessages().Select(message => message.Seq));
    }

    [Fact]
    public async Task Invio_CasellaVuotaOSoloSpazi_NonInviaNiente()
    {
        await using Fixture fixture = new();

        fixture.ViewModel.Draft = "   ";
        Assert.False(fixture.ViewModel.CanSend);
        await fixture.ViewModel.SendAsync();

        Assert.Empty(fixture.SentMessages());
    }

    [Fact]
    public async Task Draft_OltreIlLimite_VieneTroncatoEIlContatoreCompare()
    {
        await using Fixture fixture = new();

        fixture.ViewModel.Draft = new string('a', 1_801);
        Assert.True(fixture.ViewModel.ShowDraftCounter);
        Assert.False(fixture.ViewModel.IsDraftAtLimit);

        fixture.ViewModel.Draft = new string('a', 2_500);
        Assert.Equal(KryptaLimits.MessageMaxLength, fixture.ViewModel.DraftLength);
        Assert.True(fixture.ViewModel.IsDraftAtLimit);
    }

    [Fact]
    public async Task Leave_InviaLeaveETornaAUnaSelezioneStanzaPulita()
    {
        await using Fixture fixture = new();
        await fixture.Screen.Router.Navigate.Execute(fixture.ViewModel);
        await fixture.ReceiveAsync(new SystemServerMessage(
            SystemKinds.RoomCreated, "emy ha creato la stanza", Now));

        await fixture.ViewModel.LeaveCommand.Execute();

        Assert.Contains(
            fixture.Transport.SentMessages,
            json => json.Contains("\"leave\"", StringComparison.Ordinal));
        Assert.Empty(fixture.ViewModel.Messages);

        RoomViewModel room = Assert.IsType<RoomViewModel>(
            fixture.Screen.Router.NavigationStack[^1]);
        Assert.Equal(JoinAttempt.None, room.Outcome);
        Assert.Single(fixture.Screen.Router.NavigationStack);
    }

    [Fact]
    public async Task Dispose_SmetteDiAscoltareLaConnessione()
    {
        await using Fixture fixture = new();

        fixture.ViewModel.Dispose();
        await fixture.ReceiveAsync(new ParticipantsServerMessage(7));

        Assert.Equal("1/20", fixture.ViewModel.ParticipantLabel);
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

            ViewModel = new ChatViewModel(
                Screen,
                Connection,
                new SessionSettings(
                    "127.0.0.1",
                    5000,
                    UseTls: false,
                    Remember: false,
                    new ConfigLoadResult(ClientConfig.Default)),
                new JoinedServerMessage(
                    "K7X2M9", "RM-K7X2M9", "emy", 1, Created: true, 20, 1));
        }

        public TestScreen Screen { get; } = new();

        public QueuedUiDispatcher Dispatcher { get; } = new();

        public FakeWebSocketTransport Transport { get; }

        public KryptaConnection Connection { get; }

        public ChatViewModel ViewModel { get; }

        public async Task ReceiveAsync(ServerMessage message)
        {
            Transport.QueueText(JsonSerializer.Serialize(
                message,
                ProtocolJsonContext.Default.ServerMessage));

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            while (Dispatcher.PendingCount == 0)
            {
                await Task.Delay(10, timeout.Token);
            }

            Dispatcher.Drain();
        }

        public IEnumerable<ChatClientMessage> SentMessages() =>
            Transport.SentMessages
                .Select(json => JsonSerializer.Deserialize(
                    json,
                    ProtocolJsonContext.Default.ClientMessage))
                .OfType<ChatClientMessage>();

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }
}
