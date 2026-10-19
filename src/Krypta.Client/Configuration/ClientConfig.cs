namespace Krypta.Client.Configuration;

public enum ClientTheme
{
    Dark,
    Light,
}

public sealed record ServerConfig(string Host, int Port, bool UseTls);

public sealed record KnownServerConfig(
    string Host,
    int Port,
    string Sha256,
    DateOnly FirstSeen);

public sealed record UserConfig(string Nickname);

public sealed record UiConfig(
    ClientTheme Theme,
    int FontSize,
    bool RememberSettings);

public sealed record ClientConfig(
    ServerConfig Server,
    IReadOnlyList<KnownServerConfig> KnownServers,
    UserConfig User,
    UiConfig Ui)
{
    public const int CurrentVersion = 1;

    public static ClientConfig Default => new(
        new ServerConfig("127.0.0.1", 5000, UseTls: true),
        [],
        new UserConfig(string.Empty),
        new UiConfig(ClientTheme.Dark, 14, RememberSettings: true));
}

public sealed record ConfigLoadResult(
    ClientConfig Config,
    string? Warning = null,
    bool CanOverwrite = true);

public sealed record ConfigSaveResult(
    bool Saved,
    bool Skipped = false,
    string? Warning = null);
