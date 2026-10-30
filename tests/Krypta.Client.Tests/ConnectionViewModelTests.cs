using System.Net.Sockets;
using System.Net.WebSockets;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using Krypta.Client.Tests.TestDoubles;
using Krypta.Client.ViewModels;
using ReactiveUI;

namespace Krypta.Client.Tests;

public sealed class ConnectionViewModelTests
{
    [Fact]
    public async Task Initialize_ConfigValido_PrecompilaHostPortERicorda()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            """
            <KryptaConfig version="1">
              <Server><Host>chat.example</Host><Port>7443</Port><UseTls>true</UseTls></Server>
              <Ui><RememberSettings>false</RememberSettings></Ui>
            </KryptaConfig>
            """);
        await using Fixture fixture = new(temporary.ConfigPath);

        await fixture.ViewModel.Initialization;

        Assert.Equal("chat.example", fixture.ViewModel.Host);
        Assert.Equal("7443", fixture.ViewModel.Port);
        Assert.False(fixture.ViewModel.Remember);
        Assert.True(fixture.ViewModel.CanConnect);
        Assert.Equal(ConnectionStatusKind.None, fixture.ViewModel.StatusKind);
    }

    [Fact]
    public async Task Initialize_ConfigMalformato_UsaDefaultEMostraAvviso()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(temporary.ConfigPath, "<KryptaConfig>");
        await using Fixture fixture = new(temporary.ConfigPath);

        await fixture.ViewModel.Initialization;

        Assert.Equal("127.0.0.1", fixture.ViewModel.Host);
        Assert.Equal("5000", fixture.ViewModel.Port);
        Assert.Equal(ConnectionStatusKind.Warning, fixture.ViewModel.StatusKind);
        Assert.Equal(ConfigService.UnreadableWarning, fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Input_NonValido_DisabilitaConnessioneELaPortaTieneSoloCinqueCifre()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;

        fixture.ViewModel.Host = "host non valido";
        Assert.False(fixture.ViewModel.CanConnect);

        fixture.ViewModel.Host = "127.0.0.1";
        fixture.ViewModel.Port = "12a3456";
        Assert.Equal("12345", fixture.ViewModel.Port);
        Assert.True(fixture.ViewModel.CanConnect);

        fixture.ViewModel.Port = "0";
        Assert.False(fixture.ViewModel.CanConnect);
    }

    [Fact]
    public async Task Connect_DuranteTentativo_MostraStatoEDisabilitaCampiEPulsante()
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using Fixture fixture = new(temporary.ConfigPath);
        fixture.Transport.ConnectGate = gate;
        await fixture.ViewModel.Initialization;

        Task connecting = fixture.ViewModel.ConnectAsync();
        await WaitUntilAsync(() => fixture.Transport.ConnectedUri is not null);

        Assert.True(fixture.ViewModel.IsConnecting);
        Assert.False(fixture.ViewModel.CanEdit);
        Assert.False(fixture.ViewModel.CanConnect);
        Assert.Equal(ConnectionStatusKind.InProgress, fixture.ViewModel.StatusKind);
        Assert.Equal("Connessione…", fixture.ViewModel.ConnectButtonText);

        gate.SetResult();
        await connecting;
        Assert.True(fixture.ViewModel.IsConnected);
        Assert.Equal(ConnectionStatusKind.Success, fixture.ViewModel.StatusKind);
        Assert.Equal("Connesso.", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Connect_RiuscitoConRicorda_SalvaHostEPortaDopoIlSuccesso()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;
        fixture.ViewModel.Host = "chat.example";
        fixture.ViewModel.Port = "7443";
        fixture.ViewModel.Remember = true;

        await fixture.ViewModel.ConnectAsync();
        ConfigLoadResult saved = await new ConfigService(temporary.ConfigPath).LoadAsync();

        Assert.Equal(new Uri("wss://chat.example:7443/ws"), fixture.Transport.ConnectedUri);
        Assert.Equal("chat.example", saved.Config.Server.Host);
        Assert.Equal(7443, saved.Config.Server.Port);
        Assert.Equal(string.Empty, saved.Config.User.Nickname);
    }

    [Fact]
    public async Task Connect_RiuscitoSenzaRicorda_NonCreaIlFile()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;
        fixture.ViewModel.Remember = false;

        await fixture.ViewModel.ConnectAsync();

        Assert.True(fixture.ViewModel.IsConnected);
        Assert.False(File.Exists(temporary.ConfigPath));
    }

    [Fact]
    public async Task Connect_PortaChiusa_MostraErroreENonSalva()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        fixture.Transport.ConnectException = new HttpRequestException(
            "Connessione fallita",
            new SocketException((int)SocketError.ConnectionRefused));
        await fixture.ViewModel.Initialization;

        await fixture.ViewModel.ConnectAsync();

        Assert.False(fixture.ViewModel.IsConnected);
        Assert.Equal(ConnectionStatusKind.Error, fixture.ViewModel.StatusKind);
        Assert.Equal("Porta chiusa o server non in ascolto.", fixture.ViewModel.StatusMessage);
        Assert.False(File.Exists(temporary.ConfigPath));
    }

    [Theory]
    [MemberData(nameof(ConnectionErrors))]
    public async Task Connect_AltriErroriPrevisti_MostraUnMotivoSpecifico(
        Exception exception,
        string expectedMessage)
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        fixture.Transport.ConnectException = exception;
        await fixture.ViewModel.Initialization;

        await fixture.ViewModel.ConnectAsync();

        Assert.Equal(ConnectionStatusKind.Error, fixture.ViewModel.StatusKind);
        Assert.Equal(expectedMessage, fixture.ViewModel.StatusMessage);
        Assert.False(File.Exists(temporary.ConfigPath));
    }

    [Fact]
    public async Task Cleartext_VersoLoopback_SiPuoAttivareEMostraLAvvisoPermanente()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;

        Assert.True(fixture.ViewModel.CanUseCleartext);
        Assert.False(fixture.ViewModel.UseCleartext);
        Assert.False(fixture.ViewModel.ShowCleartextWarning);

        fixture.ViewModel.UseCleartext = true;

        Assert.True(fixture.ViewModel.ShowCleartextWarning);
    }

    [Fact]
    public async Task Cleartext_HostNonLoopback_SiSpegneDaSolaEDisabilitaLaSpunta()
    {
        // Il trasporto rifiuta comunque `ws://` fuori da 127.0.0.1: lasciare la spunta
        // accesa produrrebbe un errore che l'utente non ha provocato.
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;
        fixture.ViewModel.UseCleartext = true;

        fixture.ViewModel.Host = "chat.example";

        Assert.False(fixture.ViewModel.CanUseCleartext);
        Assert.False(fixture.ViewModel.UseCleartext);
        Assert.False(fixture.ViewModel.ShowCleartextWarning);
    }

    [Fact]
    public async Task Connect_InChiaro_UsaWsESalvaUseTlsFalse()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;
        fixture.ViewModel.UseCleartext = true;

        await fixture.ViewModel.ConnectAsync();

        Assert.Equal("ws://127.0.0.1:5000/ws", fixture.Transport.ConnectedUri?.ToString());
        ConfigLoadResult reloaded = await new ConfigService(temporary.ConfigPath).LoadAsync();
        Assert.False(reloaded.Config.Server.UseTls);
    }

    [Fact]
    public async Task Connect_HandshakeTlsFallito_DiceCheIlServerNonParlaTls()
    {
        // È esattamente ciò che restituisce .NET quando un ClientHello arriva a un server
        // in chiaro: prima diceva "verifica host e porta", che sono invece corretti.
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        fixture.Transport.ConnectException = new WebSocketException(
            "Impossibile effettuare la connessione al server remoto.",
            new IOException("Sincronizzazione non riuscita. Formato di pacchetto imprevisto."));
        await fixture.ViewModel.Initialization;

        await fixture.ViewModel.ConnectAsync();

        Assert.Equal(ConnectionStatusKind.Error, fixture.ViewModel.StatusKind);
        Assert.Contains("TLS", fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(
            "Connessione in chiaro",
            fixture.ViewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_InChiaro_PassaLaSceltaAllaSchermataAccount()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;
        fixture.ViewModel.UseCleartext = true;

        await fixture.ViewModel.ConnectAsync();

        AccountViewModel account = Assert.IsType<AccountViewModel>(
            fixture.Screen.Router.NavigationStack[^1]);
        Assert.Equal("127.0.0.1:5000", account.ServerLabel);
    }

    [Fact]
    public async Task Connect_ConfigIlleggibile_ConnetteMaNonSovrascriveEMostraAvviso()
    {
        using TemporaryDirectory temporary = new();
        const string malformed = "<KryptaConfig>rotto";
        await File.WriteAllTextAsync(temporary.ConfigPath, malformed);
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.ViewModel.Initialization;

        await fixture.ViewModel.ConnectAsync();

        Assert.True(fixture.ViewModel.IsConnected);
        Assert.Equal(ConnectionStatusKind.Warning, fixture.ViewModel.StatusKind);
        Assert.StartsWith("Connesso.", fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(malformed, await File.ReadAllTextAsync(temporary.ConfigPath));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public static TheoryData<Exception, string> ConnectionErrors => new()
    {
        {
            new TimeoutException(),
            "Tempo scaduto: il server non ha risposto entro 5 secondi."
        },
        {
            new HttpRequestException(
                "Host non trovato",
                new SocketException((int)SocketError.HostNotFound)),
            "Host non raggiungibile."
        },
        {
            new WebSocketException("Il server ha rifiutato il sotto-protocollo krypta.v1."),
            "Il server non accetta il protocollo krypta.v1."
        },
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(string configPath)
        {
            Transport = new FakeWebSocketTransport();
            Connection = new KryptaConnection(
                () => Transport,
                new QueuedUiDispatcher(),
                TimeProvider.System);
            ViewModel = new ConnectionViewModel(
                Screen,
                Connection,
                new ConfigService(configPath));
        }

        public TestScreen Screen { get; } = new();

        public FakeWebSocketTransport Transport { get; }

        public KryptaConnection Connection { get; }

        public ConnectionViewModel ViewModel { get; }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"krypta-connection-vm-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string ConfigPath => Path.Combine(DirectoryPath, ConfigService.FileName);

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
