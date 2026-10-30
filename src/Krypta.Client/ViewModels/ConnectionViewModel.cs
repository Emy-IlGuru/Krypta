using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Reactive;
using System.Reactive.Linq;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using ReactiveUI;

namespace Krypta.Client.ViewModels;

public enum ConnectionStatusKind
{
    None,
    InProgress,
    Success,
    Warning,
    Error,
}

public sealed class ConnectionViewModel : ViewModelBase, IRoutableViewModel
{
    private readonly KryptaConnection _Connection;
    private readonly ConfigService _ConfigService;

    private ConfigLoadResult _LoadedConfig = new(ClientConfig.Default);
    private string _Host = ClientConfig.Default.Server.Host;
    private string _Port = ClientConfig.Default.Server.Port.ToString(CultureInfo.InvariantCulture);
    private bool _Remember = ClientConfig.Default.Ui.RememberSettings;
    private bool _UseTls = ClientConfig.Default.Server.UseTls;
    private bool _IsConnecting;
    private bool _IsConnected;
    private string _StatusMessage = string.Empty;
    private ConnectionStatusKind _StatusKind;

    public ConnectionViewModel(
        IScreen hostScreen,
        KryptaConnection connection,
        ConfigService configService)
    {
        HostScreen = hostScreen;
        _Connection = connection;
        _ConfigService = configService;

        ConnectCommand = ReactiveCommand.CreateFromTask(
            ConnectAsync,
            this.WhenAnyValue(viewModel => viewModel.CanConnect));
        Initialization = LoadConfigAsync();
    }

    public string UrlPathSegment => "connection";

    public IScreen HostScreen { get; }

    public Task Initialization { get; }

    public string Host
    {
        get => _Host;
        set
        {
            this.RaiseAndSetIfChanged(ref _Host, value ?? string.Empty);

            // Un host diverso da 127.0.0.1 non può viaggiare in chiaro: il trasporto lo
            // rifiuterebbe comunque, quindi la spunta si spegne da sola invece di lasciare
            // l'utente davanti a un errore che non ha provocato.
            if (!CanUseCleartext)
            {
                _UseTls = true;
            }

            RaiseInputStateChanged();
            RaiseCleartextStateChanged();
        }
    }

    public string Port
    {
        get => _Port;
        set
        {
            string digitsOnly = new((value ?? string.Empty)
                .Where(character => character is >= '0' and <= '9')
                .Take(5)
                .ToArray());
            this.RaiseAndSetIfChanged(ref _Port, digitsOnly);
            RaiseInputStateChanged();
        }
    }

    public bool Remember
    {
        get => _Remember;
        set => this.RaiseAndSetIfChanged(ref _Remember, value);
    }

    /// <summary>
    /// Spunta di sviluppo: <c>ws://</c> invece di <c>wss://</c>. Ammessa solo verso
    /// 127.0.0.1 ([05 §5bis](../../../docs/05-client-desktop.md)) e, finché la fase 1B non
    /// è fatta, è l'<b>unico</b> modo di collegarsi al server di Krypta, che oggi non
    /// espone nessun endpoint TLS.
    /// </summary>
    public bool UseCleartext
    {
        get => !_UseTls;
        set
        {
            bool useTls = !(value && CanUseCleartext);
            if (useTls == _UseTls)
            {
                return;
            }

            _UseTls = useTls;
            RaiseCleartextStateChanged();
        }
    }

    public bool CanUseCleartext => CleartextPolicy.AllowsCleartext(Host);

    public bool CanToggleCleartext => CanEdit && CanUseCleartext;

    /// <summary>
    /// L'avviso è permanente e non nascondibile: è l'unica cosa che impedisce di usare
    /// Krypta credendolo cifrato quando non lo è.
    /// </summary>
    public bool ShowCleartextWarning => !_UseTls;

    public bool IsConnecting
    {
        get => _IsConnecting;
        private set
        {
            this.RaiseAndSetIfChanged(ref _IsConnecting, value);
            RaiseInputStateChanged();
            this.RaisePropertyChanged(nameof(ConnectButtonText));
        }
    }

    public bool IsConnected
    {
        get => _IsConnected;
        private set
        {
            this.RaiseAndSetIfChanged(ref _IsConnected, value);
            RaiseInputStateChanged();
        }
    }

    public bool CanEdit => !IsConnecting && !IsConnected;

    public bool CanConnect =>
        CanEdit
        && IsValidHost(Host)
        && TryGetPort(out _);

    public string ConnectButtonText => IsConnecting ? "Connessione…" : "Connetti";

    public string StatusMessage
    {
        get => _StatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _StatusMessage, value);
    }

    public ConnectionStatusKind StatusKind
    {
        get => _StatusKind;
        private set
        {
            this.RaiseAndSetIfChanged(ref _StatusKind, value);
            this.RaisePropertyChanged(nameof(HasStatus));
            this.RaisePropertyChanged(nameof(IsStatusInProgress));
            this.RaisePropertyChanged(nameof(IsStatusSuccess));
            this.RaisePropertyChanged(nameof(IsStatusWarning));
            this.RaisePropertyChanged(nameof(IsStatusError));
        }
    }

    public bool HasStatus => StatusKind != ConnectionStatusKind.None;

    public bool IsStatusInProgress => StatusKind == ConnectionStatusKind.InProgress;

    public bool IsStatusSuccess => StatusKind == ConnectionStatusKind.Success;

    public bool IsStatusWarning => StatusKind == ConnectionStatusKind.Warning;

    public bool IsStatusError => StatusKind == ConnectionStatusKind.Error;

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

    internal async Task ConnectAsync()
    {
        if (!CanConnect || !TryGetPort(out int port))
        {
            return;
        }

        IsConnecting = true;
        SetStatus(ConnectionStatusKind.InProgress, "Connessione in corso…");

        try
        {
            await _Connection.ConnectAsync(Host, port, _UseTls);
            IsConnected = true;

            ConfigSaveResult saveResult = await _ConfigService.SaveServerSettingsAsync(
                _LoadedConfig,
                Host,
                port,
                _UseTls,
                Remember);
            if (saveResult.Warning is not null)
            {
                SetStatus(
                    ConnectionStatusKind.Warning,
                    $"Connesso. {saveResult.Warning}");
            }
            else
            {
                SetStatus(ConnectionStatusKind.Success, "Connesso.");
            }

            // Host e porta passano di mano così come sono stati usati: se "Ricorda" era
            // spento il file non è stato riscritto e conterrebbe ancora altri valori.
            await HostScreen.Router.Navigate.Execute(new AccountViewModel(
                HostScreen,
                _Connection,
                _ConfigService,
                new SessionSettings(Host, port, _UseTls, Remember, _LoadedConfig),
                saveResult.Warning));
        }
        catch (Exception exception) when (exception is TimeoutException
            or WebSocketException
            or HttpRequestException
            or InvalidOperationException)
        {
            IsConnected = false;
            SetStatus(ConnectionStatusKind.Error, FormatConnectionError(exception, _UseTls));
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task LoadConfigAsync()
    {
        try
        {
            ConfigLoadResult loaded = await _ConfigService.LoadAsync();
            _LoadedConfig = loaded;
            Host = loaded.Config.Server.Host;
            Port = loaded.Config.Server.Port.ToString(CultureInfo.InvariantCulture);
            Remember = loaded.Config.Ui.RememberSettings;
            _UseTls = loaded.Config.Server.UseTls;
            RaiseCleartextStateChanged();

            if (loaded.Warning is not null)
            {
                SetStatus(ConnectionStatusKind.Warning, loaded.Warning);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            SetStatus(ConnectionStatusKind.Warning, ConfigService.UnreadableWarning);
        }
    }

    private bool TryGetPort(out int port) =>
        int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= 1 and <= 65535;

    private static bool IsValidHost(string host) =>
        !string.IsNullOrWhiteSpace(host)
        && host == host.Trim()
        && Uri.CheckHostName(host) is not UriHostNameType.Unknown;

    private static string FormatConnectionError(Exception exception, bool useTls)
    {
        if (exception is TimeoutException)
        {
            return "Tempo scaduto: il server non ha risposto entro 5 secondi.";
        }

        if (exception.Message.Contains("sotto-protocollo", StringComparison.OrdinalIgnoreCase))
        {
            return "Il server non accetta il protocollo krypta.v1.";
        }

        SocketException? socketException = FindInnerException<SocketException>(exception);
        if (socketException?.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return "Porta chiusa o server non in ascolto.";
        }

        if (socketException?.SocketErrorCode is SocketError.HostNotFound
            or SocketError.NoData
            or SocketError.TryAgain)
        {
            return "Host non raggiungibile.";
        }

        // Un server in chiaro che riceve un ClientHello risponde con qualcosa che non è
        // un record TLS, e la libreria lo riporta come handshake fallito. Senza questo
        // ramo il messaggio diceva "verifica host e porta", che sono invece corretti.
        if (useTls && IsTlsHandshakeFailure(exception))
        {
            return "Handshake TLS non riuscito: il server non risponde in TLS su questa "
                + "porta. Il server di Krypta non espone ancora TLS (fase 1B): spunta "
                + "«Connessione in chiaro» per collegarti a 127.0.0.1.";
        }

        return exception is InvalidOperationException
            ? exception.Message
            : "Connessione non riuscita. Verifica host e porta.";
    }

    private static bool IsTlsHandshakeFailure(Exception exception) =>
        FindInnerException<AuthenticationException>(exception) is not null
        || FindInnerException<IOException>(exception) is not null;

    private static TException? FindInnerException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private void SetStatus(ConnectionStatusKind kind, string message)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    private void RaiseCleartextStateChanged()
    {
        this.RaisePropertyChanged(nameof(UseCleartext));
        this.RaisePropertyChanged(nameof(CanUseCleartext));
        this.RaisePropertyChanged(nameof(ShowCleartextWarning));
        this.RaisePropertyChanged(nameof(CanToggleCleartext));
    }

    private void RaiseInputStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanEdit));
        this.RaisePropertyChanged(nameof(CanConnect));
        this.RaisePropertyChanged(nameof(CanToggleCleartext));
    }
}
