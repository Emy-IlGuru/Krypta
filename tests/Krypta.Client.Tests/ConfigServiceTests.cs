using Krypta.Client.Configuration;
using Krypta.Client.Services;

namespace Krypta.Client.Tests;

public sealed class ConfigServiceTests
{
    [Fact]
    public async Task Load_FileAssente_UsaDefaultSenzaAvviso()
    {
        using TemporaryDirectory temporary = new();
        ConfigService service = new(temporary.ConfigPath);

        ConfigLoadResult result = await service.LoadAsync();

        Assert.Equal(ClientConfig.Default, result.Config);
        Assert.Null(result.Warning);
        Assert.True(result.CanOverwrite);
    }

    [Fact]
    public async Task Load_XmlMalformato_UsaDefaultENonConsenteSovrascritturaAutomatica()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(temporary.ConfigPath, "<KryptaConfig>");
        ConfigService service = new(temporary.ConfigPath);

        ConfigLoadResult result = await service.LoadAsync();

        Assert.Equal(ClientConfig.Default, result.Config);
        Assert.Equal(ConfigService.UnreadableWarning, result.Warning);
        Assert.False(result.CanOverwrite);
    }

    [Fact]
    public async Task Load_VersioneFutura_UsaDefaultEAvvisa()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            "<KryptaConfig version=\"2\"><Server><Host>chat.example</Host></Server></KryptaConfig>");
        ConfigService service = new(temporary.ConfigPath);

        ConfigLoadResult result = await service.LoadAsync();

        Assert.Equal(ClientConfig.Default, result.Config);
        Assert.Equal(ConfigService.NewerVersionWarning, result.Warning);
        Assert.False(result.CanOverwrite);
    }

    [Fact]
    public async Task Load_CampiMancantiONonValidi_ApplicaDefaultSoloAiCampiInteressati()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            """
            <KryptaConfig version="1">
              <Server><Host>chat.example</Host><Port>70000</Port><UseTls>false</UseTls></Server>
              <User><Nickname>SYSTEM</Nickname></User>
              <Ui><Theme>light</Theme><FontSize>99</FontSize></Ui>
            </KryptaConfig>
            """);
        ConfigService service = new(temporary.ConfigPath);

        ConfigLoadResult result = await service.LoadAsync();

        Assert.Equal("chat.example", result.Config.Server.Host);
        Assert.Equal(5000, result.Config.Server.Port);
        Assert.True(result.Config.Server.UseTls);
        Assert.Equal(string.Empty, result.Config.User.Nickname);
        Assert.Equal(ClientTheme.Light, result.Config.Ui.Theme);
        Assert.Equal(14, result.Config.Ui.FontSize);
        Assert.True(result.Config.Ui.RememberSettings);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Load_KnownServersMalformati_IgnoraSoloLeVociNonValide()
    {
        using TemporaryDirectory temporary = new();
        string fingerprint = string.Join(':', Enumerable.Repeat("AB", 32));
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            $$"""
            <KryptaConfig version="1">
              <KnownServers>
                <Server host="chat.example" port="5000" sha256="{{fingerprint}}" firstSeen="2026-09-08" />
                <Server host="bad host" port="0" sha256="no" firstSeen="mai" />
              </KnownServers>
            </KryptaConfig>
            """);
        ConfigService service = new(temporary.ConfigPath);

        ConfigLoadResult result = await service.LoadAsync();

        KnownServerConfig known = Assert.Single(result.Config.KnownServers);
        Assert.Equal("chat.example", known.Host);
        Assert.Equal(fingerprint, known.Sha256);
    }

    [Fact]
    public async Task SaveRemembered_FileAssente_CreaUtf8ERileggeLePreferenze()
    {
        using TemporaryDirectory temporary = new();
        ConfigService service = new(temporary.ConfigPath);
        ConfigLoadResult loaded = await service.LoadAsync();

        ConfigSaveResult saved = await service.SaveRememberedAsync(
            loaded,
            "chat.example",
            7443,
            "  emy  ",
            rememberRequested: true);
        ConfigLoadResult reloaded = await service.LoadAsync();

        Assert.True(saved.Saved);
        Assert.Equal("chat.example", reloaded.Config.Server.Host);
        Assert.Equal(7443, reloaded.Config.Server.Port);
        Assert.Equal("emy", reloaded.Config.User.Nickname);
        byte[] bytes = await File.ReadAllBytesAsync(temporary.ConfigPath);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public async Task SaveRemembered_AggiornaSoloHostPortNickname_EPreservaIlResto()
    {
        using TemporaryDirectory temporary = new();
        string fingerprint = string.Join(':', Enumerable.Repeat("CD", 32));
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            $$"""
            <KryptaConfig version="1">
              <Server><Host>127.0.0.1</Host><Port>5000</Port><UseTls>false</UseTls></Server>
              <KnownServers><Server host="old.example" port="5000" sha256="{{fingerprint}}" firstSeen="2026-09-08" /></KnownServers>
              <User><Nickname>old</Nickname></User>
              <Ui><Theme>light</Theme><FontSize>18</FontSize><RememberSettings>true</RememberSettings></Ui>
            </KryptaConfig>
            """);
        ConfigService service = new(temporary.ConfigPath);
        ConfigLoadResult loaded = await service.LoadAsync();

        ConfigSaveResult saved = await service.SaveRememberedAsync(
            loaded,
            "127.0.0.1",
            6000,
            "new",
            rememberRequested: true);
        ConfigLoadResult reloaded = await service.LoadAsync();

        Assert.True(saved.Saved);
        Assert.False(reloaded.Config.Server.UseTls);
        Assert.Equal(ClientTheme.Light, reloaded.Config.Ui.Theme);
        Assert.Equal(18, reloaded.Config.Ui.FontSize);
        Assert.Single(reloaded.Config.KnownServers);
    }

    [Fact]
    public async Task Load_ThemeNumerico_UsaDarkInveceDiInterpretareLEnum()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            "<KryptaConfig version=\"1\"><Ui><Theme>1</Theme></Ui></KryptaConfig>");
        ConfigService service = new(temporary.ConfigPath);

        ConfigLoadResult result = await service.LoadAsync();

        Assert.Equal(ClientTheme.Dark, result.Config.Ui.Theme);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SaveRemembered_SenzaEntrambiIConsensi_NonScrive(
        bool configAllowsRemembering,
        bool userRequestedRemembering)
    {
        using TemporaryDirectory temporary = new();
        ConfigService service = new(temporary.ConfigPath);
        ClientConfig config = ClientConfig.Default with
        {
            Ui = ClientConfig.Default.Ui with { RememberSettings = configAllowsRemembering },
        };

        ConfigSaveResult result = await service.SaveRememberedAsync(
            new ConfigLoadResult(config),
            "chat.example",
            5000,
            "emy",
            userRequestedRemembering);

        Assert.True(result.Skipped);
        Assert.False(File.Exists(temporary.ConfigPath));
    }

    [Fact]
    public async Task SaveRemembered_DopoXmlMalformato_NonSovrascriveIlFile()
    {
        using TemporaryDirectory temporary = new();
        const string malformed = "<KryptaConfig>rotto";
        await File.WriteAllTextAsync(temporary.ConfigPath, malformed);
        ConfigService service = new(temporary.ConfigPath);
        ConfigLoadResult loaded = await service.LoadAsync();

        ConfigSaveResult result = await service.SaveRememberedAsync(
            loaded,
            "chat.example",
            5000,
            "emy",
            rememberRequested: true);

        Assert.False(result.Saved);
        Assert.Equal(ConfigService.UnsafeOverwriteWarning, result.Warning);
        Assert.Equal(malformed, await File.ReadAllTextAsync(temporary.ConfigPath));
    }

    [Fact]
    public async Task SaveRemembered_FileNonScrivibile_RestituisceAvvisoNonBloccante()
    {
        using TemporaryDirectory temporary = new();
        await File.WriteAllTextAsync(
            temporary.ConfigPath,
            "<KryptaConfig version=\"1\" />");
        ConfigService service = new(temporary.ConfigPath);
        ConfigLoadResult loaded = await service.LoadAsync();
        await using FileStream locked = new(
            temporary.ConfigPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        ConfigSaveResult result = await service.SaveRememberedAsync(
            loaded,
            "chat.example",
            5000,
            "emy",
            rememberRequested: true);

        Assert.False(result.Saved);
        Assert.Equal(ConfigService.SaveFailedWarning, result.Warning);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"krypta-client-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string ConfigPath => Path.Combine(DirectoryPath, ConfigService.FileName);

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
