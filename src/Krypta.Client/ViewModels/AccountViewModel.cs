using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media;
using Krypta.Client.Chat;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using Krypta.Shared;
using ReactiveUI;

namespace Krypta.Client.ViewModels;

/// <summary>
/// Schermata [2]. Il nickname digitato qui è una <b>richiesta</b>: quello effettivo
/// arriva dal server nel campo <c>nickname</c> di <c>joined</c> e può avere un numero
/// appeso ([05 §3](../../../docs/05-client-desktop.md)).
/// </summary>
public sealed class AccountViewModel : ViewModelBase, IRoutableViewModel
{
    private readonly KryptaConnection _Connection;
    private readonly ConfigService _ConfigService;
    private readonly SessionSettings _Session;

    private string _Nickname = string.Empty;
    private string _StatusMessage = string.Empty;

    public AccountViewModel(
        IScreen hostScreen,
        KryptaConnection connection,
        ConfigService configService,
        SessionSettings session,
        string? initialStatus = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        HostScreen = hostScreen;
        _Connection = connection;
        _ConfigService = configService;
        _Session = session;

        ContinueCommand = ReactiveCommand.CreateFromTask(
            ContinueAsync,
            this.WhenAnyValue(viewModel => viewModel.CanContinue));
        DisconnectCommand = ReactiveCommand.CreateFromTask(DisconnectAsync);

        Nickname = session.Config.Config.User.Nickname;

        // Un avviso della schermata [1] (per esempio "non ho potuto salvare il config")
        // arriverebbe troppo tardi per essere letto lì: si naviga subito dopo il successo.
        StatusMessage = initialStatus ?? string.Empty;
    }

    public string UrlPathSegment => "account";

    public IScreen HostScreen { get; }

    public string ServerLabel => _Session.ServerLabel;

    public string Nickname
    {
        get => _Nickname;
        set
        {
            this.RaiseAndSetIfChanged(ref _Nickname, value ?? string.Empty);
            RaiseNicknameStateChanged();
        }
    }

    /// <summary>Il colore che il nickname avrà in chat, nel tema in uso.</summary>
    public Color NicknameColor =>
        NicknameColors.ForNickname(Nickname, _Session.Theme);

    public IBrush NicknameBrush => new SolidColorBrush(NicknameColor);

    /// <summary>Il testo dell'anteprima: il nickname ripulito, o un segnaposto.</summary>
    public string NicknamePreview =>
        Nickname.Trim().Length == 0 ? "il tuo nome" : Nickname.Trim();

    public bool CanContinue => Problem is NicknameProblem.None;

    /// <summary>
    /// Vuoto finché non c'è niente da correggere: mentre il campo è ancora corto non ha
    /// senso accusare l'utente di un errore che sta ancora scrivendo.
    /// </summary>
    public string ValidationMessage => Problem switch
    {
        NicknameProblem.None or NicknameProblem.Empty => string.Empty,
        NicknameProblem.TooShort =>
            $"Servono almeno {KryptaLimits.NicknameMinLength} caratteri.",
        NicknameProblem.TooLong =>
            $"Al massimo {KryptaLimits.NicknameMaxLength} caratteri.",
        NicknameProblem.Reserved =>
            "Questo nome è riservato ai messaggi di sistema. Scegline un altro.",
        _ => "Ammessi lettere e numeri ASCII, «_», «.», «-» e spazi singoli.",
    };

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    public string StatusMessage
    {
        get => _StatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _StatusMessage, value);
    }

    public bool HasStatus => StatusMessage.Length > 0;

    public ReactiveCommand<Unit, Unit> ContinueCommand { get; }

    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

    /// <summary>
    /// Il nickname accettato, dopo <see cref="ContinueCommand"/>. È quello digitato, con
    /// il solo trim: la disambiguazione la fa il server, non il client.
    /// </summary>
    public string? ConfirmedNickname { get; private set; }

    private NicknameProblem Problem => NicknameRules.Validate(Nickname);

    internal async Task ContinueAsync()
    {
        if (!NicknameRules.TryNormalize(Nickname, out string? normalized))
        {
            return;
        }

        ConfirmedNickname = normalized;

        // Si salva il nickname **digitato**, non quello che il server assegnerà: non ha
        // senso ritrovarsi `emy2` precompilato al prossimo avvio, quando magari il nome
        // è tornato libero (06 §5). Host e porta sono quelli usati per connettersi.
        ConfigSaveResult saved = await _ConfigService.SaveRememberedAsync(
            _Session.Config,
            _Session.Host,
            _Session.Port,
            _Session.UseTls,
            normalized!,
            _Session.Remember);

        StatusMessage = saved.Warning ?? string.Empty;
        this.RaisePropertyChanged(nameof(HasStatus));

        // Un eventuale avviso di salvataggio l'utente l'ha già visto nella [1]: non vale
        // un secondo blocco del flusso, la configurazione è una comodità.
        await HostScreen.Router.Navigate.Execute(new RoomViewModel(
            HostScreen,
            _Connection,
            _Session,
            normalized!));
    }

    private async Task DisconnectAsync()
    {
        await _Connection.DisconnectAsync();

        // Si torna a [1] azzerando la pila: la schermata di connessione riparte pulita e
        // rilegge il config, invece di riproporre lo stato di una connessione che non c'è più.
        await HostScreen.Router.NavigateAndReset.Execute(new ConnectionViewModel(
            HostScreen,
            _Connection,
            _ConfigService));
    }

    private void RaiseNicknameStateChanged()
    {
        this.RaisePropertyChanged(nameof(NicknameColor));
        this.RaisePropertyChanged(nameof(NicknameBrush));
        this.RaisePropertyChanged(nameof(NicknamePreview));
        this.RaisePropertyChanged(nameof(CanContinue));
        this.RaisePropertyChanged(nameof(ValidationMessage));
        this.RaisePropertyChanged(nameof(HasValidationMessage));
    }
}
