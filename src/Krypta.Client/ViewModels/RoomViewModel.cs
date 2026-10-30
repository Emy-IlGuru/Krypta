using System.Reactive;
using System.Reactive.Linq;
using Krypta.Client.Configuration;
using Krypta.Client.Rooms;
using Krypta.Client.Services;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using ReactiveUI;

namespace Krypta.Client.ViewModels;

/// <summary>Come è finita la richiesta di ingresso.</summary>
public enum JoinAttempt
{
    None,
    Created,
    Pending,
}

/// <summary>
/// Schermata [3]. Non mostra e non suggerisce nessun elenco di stanze: l'unico modo di
/// conoscere un ID è che qualcuno te lo dica ([05 §4](../../../docs/05-client-desktop.md)).
/// </summary>
public sealed class RoomViewModel : ViewModelBase, IRoutableViewModel
{
    public const string DefaultJoinMessage = "Hey, mi vorrei unire alla stanza";

    private readonly KryptaConnection _Connection;
    private readonly string _Nickname;
    private readonly SessionSettings _Session;
    private readonly Func<IObservable<long>> _CountdownTicks;

    private IDisposable? _Countdown;
    private string _RoomId = string.Empty;
    private string _JoinMessage = DefaultJoinMessage;
    private string _ErrorMessage = string.Empty;
    private string _StatusMessage = string.Empty;
    private bool _IsJoining;
    private int _RetryAfterSeconds;
    private JoinAttempt _Outcome = JoinAttempt.None;
    private JoinedServerMessage? _Joined;

    public RoomViewModel(
        IScreen hostScreen,
        KryptaConnection connection,
        SessionSettings session,
        string nickname,
        Func<IObservable<long>>? countdownTicks = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        HostScreen = hostScreen;
        _Connection = connection;
        _Nickname = nickname;
        _Session = session;
        ServerLabel = session.ServerLabel;
        _CountdownTicks = countdownTicks
            ?? (static () => Observable.Interval(
                TimeSpan.FromSeconds(1),
                RxApp.MainThreadScheduler));

        JoinCommand = ReactiveCommand.CreateFromTask(
            JoinAsync,
            this.WhenAnyValue(viewModel => viewModel.CanJoin));
        GenerateIdCommand = ReactiveCommand.Create(
            () => RoomId = RoomIdGenerator.Next(),
            this.WhenAnyValue(viewModel => viewModel.CanEdit));
        ChangeNicknameCommand = ReactiveCommand.CreateFromTask(
            async () => { _ = await HostScreen.Router.NavigateBack.Execute(); },
            this.WhenAnyValue(viewModel => viewModel.CanEdit));
    }

    public string UrlPathSegment => "room";

    public IScreen HostScreen { get; }

    public string ServerLabel { get; }

    public string Nickname => _Nickname;

    /// <summary>
    /// Il campo accetta solo <c>[A-Z0-9]</c> e converte in maiuscolo <b>mentre si digita</b>:
    /// il protocollo ammette una sola forma e il server rifiuta tutto il resto, quindi senza
    /// la conversione chi digita in minuscolo riceverebbe un errore incomprensibile.
    /// Incollando testo misto, i caratteri non validi vengono scartati.
    /// </summary>
    public string RoomId
    {
        get => _RoomId;
        set
        {
            string filtered = new((value ?? string.Empty)
                .Select(char.ToUpperInvariant)
                .Where(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9')
                .Take(KryptaLimits.RoomIdLength)
                .ToArray());

            this.RaiseAndSetIfChanged(ref _RoomId, filtered);
            RaiseInputStateChanged();
        }
    }

    /// <summary>
    /// Precompilato e modificabile. Svuotarlo è legittimo: il campo semplicemente non
    /// viene inviato (RF-C-A11). Riga singola, quindi i caratteri di controllo cadono.
    /// </summary>
    public string JoinMessage
    {
        get => _JoinMessage;
        set
        {
            string singleLine = new((value ?? string.Empty)
                .Where(character => !char.IsControl(character))
                .Take(KryptaLimits.JoinMessageMaxLength)
                .ToArray());

            this.RaiseAndSetIfChanged(ref _JoinMessage, singleLine);
            this.RaisePropertyChanged(nameof(JoinMessageLength));
            this.RaisePropertyChanged(nameof(IsJoinMessageAtLimit));
        }
    }

    public int JoinMessageLength => _JoinMessage.Length;

    public int JoinMessageMaxLength => KryptaLimits.JoinMessageMaxLength;

    public bool IsJoinMessageAtLimit =>
        _JoinMessage.Length >= KryptaLimits.JoinMessageMaxLength;

    public bool CanEdit => !_IsJoining && _Outcome is JoinAttempt.None;

    public bool CanJoin =>
        CanEdit
        && _RoomId.Length == KryptaLimits.RoomIdLength
        && _RetryAfterSeconds == 0;

    public string JoinButtonText => _IsJoining ? "Attendi…" : "Entra";

    /// <summary>
    /// Durante il blocco il testo si ricalcola a ogni secondo, così il conto alla rovescia
    /// si vede senza una seconda etichetta.
    /// </summary>
    public string ErrorMessage => _RetryAfterSeconds > 0
        ? $"Troppi tentativi di ingresso. Riprova tra {_RetryAfterSeconds} "
            + (_RetryAfterSeconds == 1 ? "secondo." : "secondi.")
        : _ErrorMessage;

    public bool HasError => ErrorMessage.Length > 0;

    public int RetryAfterSeconds => _RetryAfterSeconds;

    public string StatusMessage => _StatusMessage;

    public bool HasStatus => _StatusMessage.Length > 0;

    public JoinAttempt Outcome
    {
        get => _Outcome;
        private set
        {
            this.RaiseAndSetIfChanged(ref _Outcome, value);
            RaiseInputStateChanged();
        }
    }

    /// <summary>L'ID effettivamente usato, disponibile dopo un esito.</summary>
    public string? JoinedRoomId { get; private set; }

    public ReactiveCommand<Unit, Unit> JoinCommand { get; }

    public ReactiveCommand<Unit, string> GenerateIdCommand { get; }

    public ReactiveCommand<Unit, Unit> ChangeNicknameCommand { get; }

    internal async Task JoinAsync()
    {
        if (!CanJoin)
        {
            return;
        }

        IsJoining = true;
        SetError(string.Empty);
        SetStatus(string.Empty);

        // Gli handler si registrano solo per la durata della richiesta e si tolgono da
        // soli: nessuna sottoscrizione sopravvive al ViewModel.
        TaskCompletionSource answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnJoined(object? sender, JoinedServerMessage message)
        {
            _Joined = message;
            JoinedRoomId = message.RoomId;
            Outcome = JoinAttempt.Created;
            SetStatus($"Sei nella stanza {message.RoomId}, come «{message.Nickname}».");
            answered.TrySetResult();
        }

        void OnPending(object? sender, PendingServerMessage message)
        {
            JoinedRoomId = message.RoomId;
            Outcome = JoinAttempt.Pending;
            SetStatus(
                "Richiesta inviata. Aspetta che qualcuno già dentro ti accetti: "
                + $"scade fra {message.ExpiresInSeconds} secondi.");
            answered.TrySetResult();
        }

        void OnError(object? sender, ErrorServerMessage message)
        {
            SetError(DescribeError(message));
            if (message.Code == ErrorCodes.JoinRateLimited)
            {
                StartCountdown(message.RetryAfterSeconds ?? 0);
            }

            answered.TrySetResult();
        }

        void OnDisconnected(object? sender, KryptaDisconnectedEventArgs args)
        {
            SetError(args.Message);
            answered.TrySetResult();
        }

        _Connection.Joined += OnJoined;
        _Connection.PendingStarted += OnPending;
        _Connection.ErrorReceived += OnError;
        _Connection.Disconnected += OnDisconnected;

        try
        {
            string? message = JoinMessage.Trim();
            await _Connection.JoinAsync(
                RoomId,
                _Nickname,
                message.Length == 0 ? null : message);

            // Nessun timeout: il server risponde sempre, e se il socket muore arriva
            // `Disconnected`, che è già uno degli esiti attesi qui sopra.
            await answered.Task;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ObjectDisposedException)
        {
            SetError("La connessione al server non è più attiva.");
        }
        finally
        {
            _Connection.Joined -= OnJoined;
            _Connection.PendingStarted -= OnPending;
            _Connection.ErrorReceived -= OnError;
            _Connection.Disconnected -= OnDisconnected;
            IsJoining = false;
        }

        if (_Outcome is JoinAttempt.Created && _Joined is not null)
        {
            await HostScreen.Router.Navigate.Execute(new ChatViewModel(
                HostScreen,
                _Connection,
                _Session,
                _Joined));
        }
    }

    /// <summary>Un secondo di conto alla rovescia. Guidato dall'esterno, per i test.</summary>
    internal void CountdownTick()
    {
        if (_RetryAfterSeconds == 0)
        {
            return;
        }

        _RetryAfterSeconds--;
        this.RaisePropertyChanged(nameof(RetryAfterSeconds));
        this.RaisePropertyChanged(nameof(ErrorMessage));
        this.RaisePropertyChanged(nameof(HasError));
        RaiseInputStateChanged();

        if (_RetryAfterSeconds == 0)
        {
            StopCountdown();
        }
    }

    private bool IsJoining
    {
        get => _IsJoining;
        set
        {
            this.RaiseAndSetIfChanged(ref _IsJoining, value);
            this.RaisePropertyChanged(nameof(JoinButtonText));
            RaiseInputStateChanged();
        }
    }

    private static string DescribeError(ErrorServerMessage message) => message.Code switch
    {
        ErrorCodes.RoomFull =>
            $"La stanza è piena ({KryptaLimits.MaxParticipantsPerRoom}/{KryptaLimits.MaxParticipantsPerRoom}).",
        ErrorCodes.WaitingRoomFull =>
            "La sala d'attesa di questa stanza è piena. Riprova fra poco.",
        ErrorCodes.JoinRateLimited => string.Empty,
        ErrorCodes.InvalidRoomId => "ID stanza non valido.",
        ErrorCodes.InvalidNickname => "Il server ha rifiutato il nickname.",
        ErrorCodes.InvalidJoinMessage => "Il messaggio di richiesta non è valido.",
        _ => "Ingresso non riuscito. Riprova.",
    };

    private void StartCountdown(int seconds)
    {
        StopCountdown();
        _RetryAfterSeconds = Math.Max(0, seconds);
        this.RaisePropertyChanged(nameof(RetryAfterSeconds));
        this.RaisePropertyChanged(nameof(ErrorMessage));
        this.RaisePropertyChanged(nameof(HasError));
        RaiseInputStateChanged();

        if (_RetryAfterSeconds > 0)
        {
            _Countdown = _CountdownTicks().Subscribe(_ => CountdownTick());
        }
    }

    private void StopCountdown()
    {
        _Countdown?.Dispose();
        _Countdown = null;
    }

    private void SetError(string message)
    {
        _ErrorMessage = message;
        this.RaisePropertyChanged(nameof(ErrorMessage));
        this.RaisePropertyChanged(nameof(HasError));
    }

    private void SetStatus(string message)
    {
        _StatusMessage = message;
        this.RaisePropertyChanged(nameof(StatusMessage));
        this.RaisePropertyChanged(nameof(HasStatus));
    }

    private void RaiseInputStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanEdit));
        this.RaisePropertyChanged(nameof(CanJoin));
    }
}
