using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Krypta.Client.Configuration;
using Krypta.Shared;

namespace Krypta.Client.Services;

public sealed partial class ConfigService
{
    public const string FileName = "krypta.config.xml";
    public const string UnreadableWarning =
        "Configurazione non leggibile, uso i valori predefiniti.";
    public const string NewerVersionWarning =
        "La configurazione usa una versione più recente, uso i valori predefiniti.";
    public const string SaveFailedWarning =
        "Impossibile salvare la configurazione. La sessione continua senza modificare il file.";
    public const string UnsafeOverwriteWarning =
        "La configurazione non leggibile non è stata sovrascritta automaticamente.";

    private static readonly HashSet<string> ReservedNicknames = new(
        ["STANZA", "KRYPTA", "SISTEMA", "SYSTEM", "SERVER", "ROOM"],
        StringComparer.OrdinalIgnoreCase);

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? Path.Combine(AppContext.BaseDirectory, FileName);
    }

    public string ConfigPath { get; }

    public async Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigPath))
        {
            return new ConfigLoadResult(ClientConfig.Default);
        }

        try
        {
            await using FileStream stream = new(
                ConfigPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            XmlReaderSettings settings = new()
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            using XmlReader reader = XmlReader.Create(stream, settings);
            XDocument document = await XDocument.LoadAsync(
                reader,
                LoadOptions.None,
                cancellationToken);

            return Parse(document);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or XmlException)
        {
            return new ConfigLoadResult(
                ClientConfig.Default,
                UnreadableWarning,
                CanOverwrite: false);
        }
    }

    public async Task<ConfigSaveResult> SaveRememberedAsync(
        ConfigLoadResult loaded,
        string host,
        int port,
        string nickname,
        bool rememberRequested,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        if (!loaded.Config.Ui.RememberSettings || !rememberRequested)
        {
            return new ConfigSaveResult(Saved: false, Skipped: true);
        }

        if (!loaded.CanOverwrite)
        {
            return new ConfigSaveResult(Saved: false, Warning: UnsafeOverwriteWarning);
        }

        if (!IsValidHost(host)
            || port is < 1 or > 65535
            || (!loaded.Config.Server.UseTls && !IsCleartextLoopback(host))
            || !TryNormalizeNickname(nickname, out string normalizedNickname))
        {
            return new ConfigSaveResult(Saved: false, Warning: SaveFailedWarning);
        }

        ClientConfig updated = loaded.Config with
        {
            Server = loaded.Config.Server with { Host = host, Port = port },
            User = loaded.Config.User with { Nickname = normalizedNickname },
        };

        return await SaveAsync(updated, cancellationToken);
    }

    private static ConfigLoadResult Parse(XDocument document)
    {
        XElement? root = document.Root;
        if (root?.Name != "KryptaConfig"
            || !int.TryParse(root.Attribute("version")?.Value, out int version)
            || version < ClientConfig.CurrentVersion)
        {
            return new ConfigLoadResult(
                ClientConfig.Default,
                UnreadableWarning,
                CanOverwrite: false);
        }

        if (version > ClientConfig.CurrentVersion)
        {
            return new ConfigLoadResult(
                ClientConfig.Default,
                NewerVersionWarning,
                CanOverwrite: false);
        }

        ClientConfig defaults = ClientConfig.Default;
        XElement? server = root.Element("Server");
        XElement? user = root.Element("User");
        XElement? ui = root.Element("Ui");

        string host = ReadValid(
            server?.Element("Host")?.Value,
            IsValidHost,
            defaults.Server.Host);
        int port = ReadInt(
            server?.Element("Port")?.Value,
            1,
            65535,
            defaults.Server.Port);
        bool useTls = ReadBool(
            server?.Element("UseTls")?.Value,
            defaults.Server.UseTls);
        if (!useTls && !IsCleartextLoopback(host))
        {
            useTls = defaults.Server.UseTls;
        }
        string nickname = TryNormalizeNickname(
            user?.Element("Nickname")?.Value,
            out string normalizedNickname)
            ? normalizedNickname
            : defaults.User.Nickname;
        ClientTheme theme = ParseTheme(
            ui?.Element("Theme")?.Value,
            defaults.Ui.Theme);
        int fontSize = ReadInt(
            ui?.Element("FontSize")?.Value,
            10,
            24,
            defaults.Ui.FontSize);
        bool rememberSettings = ReadBool(
            ui?.Element("RememberSettings")?.Value,
            defaults.Ui.RememberSettings);

        return new ConfigLoadResult(new ClientConfig(
            new ServerConfig(host, port, useTls),
            ParseKnownServers(root.Element("KnownServers")),
            new UserConfig(nickname),
            new UiConfig(theme, fontSize, rememberSettings)));
    }

    private async Task<ConfigSaveResult> SaveAsync(
        ClientConfig config,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(ConfigPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new ConfigSaveResult(Saved: false, Warning: SaveFailedWarning);
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(ConfigPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            XDocument document = CreateDocument(config);
            XmlWriterSettings settings = new()
            {
                Async = true,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                NewLineChars = Environment.NewLine,
            };

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                await document.SaveAsync(writer, cancellationToken);
                await writer.FlushAsync();
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, ConfigPath, overwrite: true);
            return new ConfigSaveResult(Saved: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return new ConfigSaveResult(Saved: false, Warning: SaveFailedWarning);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                // Il file temporaneo non contiene dati di chat; la pulizia non deve bloccare la sessione.
            }
        }
    }

    private static XDocument CreateDocument(ClientConfig config) => new(
        new XDeclaration("1.0", "utf-8", null),
        new XElement(
            "KryptaConfig",
            new XAttribute("version", ClientConfig.CurrentVersion),
            new XElement(
                "Server",
                new XElement("Host", config.Server.Host),
                new XElement("Port", config.Server.Port),
                new XElement("UseTls", config.Server.UseTls.ToString().ToLowerInvariant())),
            new XElement(
                "KnownServers",
                config.KnownServers.Select(known => new XElement(
                    "Server",
                    new XAttribute("host", known.Host),
                    new XAttribute("port", known.Port),
                    new XAttribute("sha256", known.Sha256),
                    new XAttribute(
                        "firstSeen",
                        known.FirstSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))),
            new XElement("User", new XElement("Nickname", config.User.Nickname)),
            new XElement(
                "Ui",
                new XElement("Theme", config.Ui.Theme.ToString().ToLowerInvariant()),
                new XElement("FontSize", config.Ui.FontSize),
                new XElement(
                    "RememberSettings",
                    config.Ui.RememberSettings.ToString().ToLowerInvariant()))));

    private static IReadOnlyList<KnownServerConfig> ParseKnownServers(XElement? element)
    {
        if (element is null)
        {
            return [];
        }

        List<KnownServerConfig> knownServers = [];
        foreach (XElement candidate in element.Elements("Server"))
        {
            string? host = candidate.Attribute("host")?.Value;
            string? sha256 = candidate.Attribute("sha256")?.Value;
            if (!IsValidHost(host)
                || !int.TryParse(candidate.Attribute("port")?.Value, out int port)
                || port is < 1 or > 65535
                || sha256 is null
                || !Sha256Pattern().IsMatch(sha256)
                || !DateOnly.TryParseExact(
                    candidate.Attribute("firstSeen")?.Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly firstSeen))
            {
                continue;
            }

            knownServers.Add(new KnownServerConfig(
                host!,
                port,
                sha256.ToUpperInvariant(),
                firstSeen));
        }

        return knownServers;
    }

    private static bool IsValidHost(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value == value.Trim()
        && Uri.CheckHostName(value) is not UriHostNameType.Unknown;

    private static bool IsCleartextLoopback(string host) =>
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal);

    private static bool TryNormalizeNickname(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < KryptaLimits.NicknameMinLength
            or > KryptaLimits.NicknameMaxLength
            || ReservedNicknames.Contains(normalized))
        {
            return false;
        }

        bool previousWasSpace = false;
        foreach (char character in normalized)
        {
            bool isAsciiToken = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '.'
                or '-';
            if (isAsciiToken)
            {
                previousWasSpace = false;
                continue;
            }

            if (character != ' ' || previousWasSpace)
            {
                return false;
            }

            previousWasSpace = true;
        }

        return true;
    }

    private static T ReadValid<T>(T? value, Func<T?, bool> predicate, T fallback)
        where T : class => predicate(value) ? value! : fallback;

    private static int ReadInt(string? value, int minimum, int maximum, int fallback) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
        && parsed >= minimum
        && parsed <= maximum
            ? parsed
            : fallback;

    private static bool ReadBool(string? value, bool fallback) =>
        bool.TryParse(value, out bool parsed) ? parsed : fallback;

    private static ClientTheme ParseTheme(string? value, ClientTheme fallback)
    {
        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return ClientTheme.Dark;
        }

        return string.Equals(value, "light", StringComparison.OrdinalIgnoreCase)
            ? ClientTheme.Light
            : fallback;
    }

    [GeneratedRegex("^(?:[0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
