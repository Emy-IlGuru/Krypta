using System.Reactive.Linq;
using Krypta.Client.Chat;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using Krypta.Client.Tests.TestDoubles;
using Krypta.Client.ViewModels;
using ReactiveUI;

namespace Krypta.Client.Tests;

/// <summary>
/// Punto 1A.5, schermata [2]: nickname, anteprima colorata e avvertenza sul numero
/// automatico ([05 §3](../../docs/05-client-desktop.md)).
/// </summary>
public sealed class AccountViewModelTests
{
    [Fact]
    public async Task Init_ConfigConNickname_PrecompilaIlCampo()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath, nickname: "emy");

        Assert.Equal("emy", fixture.ViewModel.Nickname);
        Assert.True(fixture.ViewModel.CanContinue);
        Assert.Equal("127.0.0.1:5000", fixture.ViewModel.ServerLabel);
    }

    [Fact]
    public async Task Init_SenzaNickname_AvantiDisabilitatoSenzaAccusareLUtente()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);

        Assert.False(fixture.ViewModel.CanContinue);
        Assert.False(fixture.ViewModel.HasValidationMessage);
        Assert.Equal("il tuo nome", fixture.ViewModel.NicknamePreview);
    }

    [Theory]
    [InlineData("ab", "Servono almeno 3 caratteri.")]
    [InlineData("STANZA", "Questo nome è riservato ai messaggi di sistema. Scegline un altro.")]
    [InlineData("krypta", "Questo nome è riservato ai messaggi di sistema. Scegline un altro.")]
    [InlineData("émy", "Ammessi lettere e numeri ASCII, «_», «.», «-» e spazi singoli.")]
    [InlineData("emy  luca", "Ammessi lettere e numeri ASCII, «_», «.», «-» e spazi singoli.")]
    public async Task Nickname_NonValido_DisabilitaAvantiEDiceCosaCorreggere(
        string nickname,
        string expected)
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);

        fixture.ViewModel.Nickname = nickname;

        Assert.False(fixture.ViewModel.CanContinue);
        Assert.Equal(expected, fixture.ViewModel.ValidationMessage);
    }

    [Theory]
    [InlineData("emy")]
    [InlineData("  emy  ")]
    [InlineData("mario.rossi")]
    [InlineData("anna maria")]
    public async Task Nickname_Valido_AbilitaAvanti(string nickname)
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);

        fixture.ViewModel.Nickname = nickname;

        Assert.True(fixture.ViewModel.CanContinue);
        Assert.False(fixture.ViewModel.HasValidationMessage);
    }

    [Fact]
    public async Task Anteprima_UsaLoStessoColoreCheAvraInChat()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);

        fixture.ViewModel.Nickname = "luca";

        Assert.Equal(
            NicknameColors.ForNickname("luca", ClientTheme.Dark),
            fixture.ViewModel.NicknameColor);
        Assert.Equal("luca", fixture.ViewModel.NicknamePreview);
    }

    [Fact]
    public async Task Anteprima_SegueIlTemaDelConfig()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            """
            <KryptaConfig version="1">
              <Ui><Theme>light</Theme></Ui>
            </KryptaConfig>
            """);
        await using Fixture fixture = new(temporary.ConfigPath, loadFromFile: true);

        fixture.ViewModel.Nickname = "luca";

        Assert.Equal(
            NicknameColors.ForNickname("luca", ClientTheme.Light),
            fixture.ViewModel.NicknameColor);
        Assert.NotEqual(
            NicknameColors.ForNickname("luca", ClientTheme.Dark),
            fixture.ViewModel.NicknameColor);
    }

    [Fact]
    public async Task Continue_SalvaIlNicknameDigitatoConIlSoloTrim()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        fixture.ViewModel.Nickname = "  emy  ";

        await fixture.ViewModel.ContinueAsync();

        ConfigLoadResult reloaded = await new ConfigService(temporary.ConfigPath).LoadAsync();
        Assert.Equal("emy", reloaded.Config.User.Nickname);
        Assert.Equal("emy", fixture.ViewModel.ConfirmedNickname);
        Assert.False(fixture.ViewModel.HasStatus);
    }

    [Fact]
    public async Task Continue_ScriveHostEPortaDellaConnessione_NonQuelliDelFile()
    {
        // Il file contiene ancora i valori vecchi se la [1] non li ha salvati: partire da
        // lì riporterebbe indietro host e porta insieme al nickname.
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            """
            <KryptaConfig version="1">
              <Server><Host>vecchio.example</Host><Port>1234</Port><UseTls>true</UseTls></Server>
            </KryptaConfig>
            """);
        await using Fixture fixture = new(
            temporary.ConfigPath,
            loadFromFile: true,
            host: "chat.example",
            port: 7443);
        fixture.ViewModel.Nickname = "emy";

        await fixture.ViewModel.ContinueAsync();

        ConfigLoadResult reloaded = await new ConfigService(temporary.ConfigPath).LoadAsync();
        Assert.Equal("chat.example", reloaded.Config.Server.Host);
        Assert.Equal(7443, reloaded.Config.Server.Port);
        Assert.Equal("emy", reloaded.Config.User.Nickname);
    }

    [Fact]
    public async Task Continue_SenzaRicorda_ConfermaIlNicknameMaNonScriveIlFile()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath, remember: false);
        fixture.ViewModel.Nickname = "emy";

        await fixture.ViewModel.ContinueAsync();

        Assert.Equal("emy", fixture.ViewModel.ConfirmedNickname);
        Assert.False(File.Exists(temporary.ConfigPath));
    }

    [Fact]
    public async Task Continue_NicknameNonValido_NonConfermaENonScrive()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        fixture.ViewModel.Nickname = "ab";

        await fixture.ViewModel.ContinueAsync();

        Assert.Null(fixture.ViewModel.ConfirmedNickname);
        Assert.False(File.Exists(temporary.ConfigPath));
    }

    [Fact]
    public async Task Continue_SalvataggioImpossibile_MostraAvvisoSenzaBloccare()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(temporary.ConfigPath, "<KryptaConfig>rotto");
        await using Fixture fixture = new(temporary.ConfigPath, loadFromFile: true);
        fixture.ViewModel.Nickname = "emy";

        await fixture.ViewModel.ContinueAsync();

        Assert.Equal("emy", fixture.ViewModel.ConfirmedNickname);
        Assert.Equal(ConfigService.UnsafeOverwriteWarning, fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Init_AvvisoArrivatoDallaSchermata1_RestaVisibile()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(
            temporary.ConfigPath,
            initialStatus: ConfigService.SaveFailedWarning);

        Assert.True(fixture.ViewModel.HasStatus);
        Assert.Equal(ConfigService.SaveFailedWarning, fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Disconnect_ChiudeIlSocketETornaAllaConnessioneAzzerandoLaPila()
    {
        using TemporaryDirectory temporary = new();
        await using Fixture fixture = new(temporary.ConfigPath);
        await fixture.Screen.Router.Navigate.Execute(fixture.ViewModel);

        await fixture.ViewModel.DisconnectCommand.Execute();

        Assert.Single(fixture.Screen.Router.NavigationStack);
        Assert.IsType<ConnectionViewModel>(fixture.Screen.Router.NavigationStack[0]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(
            string configPath,
            string? nickname = null,
            bool remember = true,
            bool loadFromFile = false,
            string host = "127.0.0.1",
            int port = 5000,
            string? initialStatus = null)
        {
            Connection = new KryptaConnection(
                () => new FakeWebSocketTransport(),
                new QueuedUiDispatcher(),
                TimeProvider.System);
            ConfigService configService = new(configPath);

            ConfigLoadResult loaded = loadFromFile
                ? configService.LoadAsync().GetAwaiter().GetResult()
                : new ConfigLoadResult(ClientConfig.Default with
                {
                    User = new UserConfig(nickname ?? string.Empty),
                });

            ViewModel = new AccountViewModel(
                Screen,
                Connection,
                configService,
                new SessionSettings(host, port, UseTls: true, remember, loaded),
                initialStatus);
        }

        public TestScreen Screen { get; } = new();

        public KryptaConnection Connection { get; }

        public AccountViewModel ViewModel { get; }

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
                $"krypta-account-vm-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string ConfigPath => Path.Combine(DirectoryPath, ConfigService.FileName);

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
