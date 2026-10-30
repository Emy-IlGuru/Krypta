using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Krypta.Client.Chat;
using Krypta.Client.Configuration;
using Krypta.Client.Services;
using Krypta.Shared;
using Krypta.Shared.Protocol;
using ReactiveUI;

namespace Krypta.Client.ViewModels;

/// <summary>
/// Schermata [4]. Tre fasce: intestazione, area messaggi, composizione
/// ([05 §5](../../../docs/05-client-desktop.md)).
/// </summary>
public sealed class ChatViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private readonly KryptaConnection _Connection;
    private readonly SessionSettings _Session;

    private string _RoomName;
    private string _Draft = string.Empty;
    private int _ParticipantCount;
    private int _OutgoingSeq;
    private int _KeyEpoch;
    private bool _Disposed;

    public ChatViewModel(
        IScreen hostScreen,
        KryptaConnection connection,
        SessionSettings session,
        JoinedServerMessage joined)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(joined);

        HostScreen = hostScreen;
        _Connection = connection;
        _Session = session;

        RoomId = joined.RoomId;
        _RoomName = joined.RoomName;
        MyNickname = joined.Nickname;
        _ParticipantCount = joined.Participants;
        MaxParticipants = joined.MaxParticipants;
        _KeyEpoch = joined.KeyEpoch;

        SendCommand = ReactiveCommand.CreateFromTask(
            SendAsync,
            this.WhenAnyValue(viewModel => viewModel.CanSend));
        LeaveCommand = ReactiveCommand.CreateFromTask(LeaveAsync);

        _Connection.MessageReceived += OnMessage;
        _Connection.SystemReceived += OnSystem;
        _Connection.ClearReceived += OnClear;
        _Connection.ParticipantsChanged += OnParticipants;
        _Connection.Disconnected += OnDisconnected;
    }

    public string UrlPathSegment => "chat";

    public IScreen HostScreen { get; }

    public string RoomId { get; }

    public string MyNickname { get; }

    public int MaxParticipants { get; }

    /// <summary>
    /// Una collezione sola per le tre origini, così l'ordine cronologico è garantito senza
    /// fondere niente ([05 §5.3](../../../docs/05-client-desktop.md)).
    /// </summary>
    public ObservableCollection<ChatLine> Messages { get; } = [];

    public string RoomName
    {
        get => _RoomName;
        private set => this.RaiseAndSetIfChanged(ref _RoomName, value);
    }

    public int ParticipantCount
    {
        get => _ParticipantCount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _ParticipantCount, value);
            this.RaisePropertyChanged(nameof(ParticipantLabel));
        }
    }

    public string ParticipantLabel => $"{_ParticipantCount}/{MaxParticipants}";

    /// <summary>
    /// Finché la fase 1B non c'è, i messaggi viaggiano in chiaro e va detto sempre, non
    /// dietro un'icona ([05 §5bis](../../../docs/05-client-desktop.md)).
    /// </summary>
    public string EncryptionLabel => _Session.UseTls
        ? "Trasporto cifrato, messaggi ancora in chiaro (fase 1A)"
        : "Connessione non cifrata";

    public string Draft
    {
        get => _Draft;
        set
        {
            string capped = (value ?? string.Empty).Length > KryptaLimits.MessageMaxLength
                ? (value ?? string.Empty)[..KryptaLimits.MessageMaxLength]
                : value ?? string.Empty;

            this.RaiseAndSetIfChanged(ref _Draft, capped);
            this.RaisePropertyChanged(nameof(DraftLength));
            this.RaisePropertyChanged(nameof(ShowDraftCounter));
            this.RaisePropertyChanged(nameof(IsDraftAtLimit));
            this.RaisePropertyChanged(nameof(CanSend));
        }
    }

    public int DraftLength => _Draft.Length;

    public int DraftMaxLength => KryptaLimits.MessageMaxLength;

    /// <summary>Il contatore compare solo in vista del limite, non sempre.</summary>
    public bool ShowDraftCounter => _Draft.Length > 1_800;

    public bool IsDraftAtLimit => _Draft.Length >= KryptaLimits.MessageMaxLength;

    public bool CanSend => _Draft.Trim().Length > 0;

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    public ReactiveCommand<Unit, Unit> LeaveCommand { get; }

    internal async Task SendAsync()
    {
        string text = _Draft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        // La casella si svuota subito, ma il messaggio comparirà solo quando torna dal
        // server: è l'eco a dire che è davvero partito (RF-C-19).
        Draft = string.Empty;

        try
        {
            await _Connection.SendMessageAsync(
                ++_OutgoingSeq,
                _KeyEpoch,
                ClearEnvelope.ClearNonce,
                ClearEnvelope.Wrap(text));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ObjectDisposedException)
        {
            Add(ChatEntry.Local("Messaggio non inviato: la connessione non è attiva.",
                DateTimeOffset.UtcNow));
        }
    }

    internal void OnMessage(object? sender, ChatServerMessage message)
    {
        // Un involucro che non è stato scritto da noi non si mostra come testo: diventa
        // la riga «non verificato». In 1B sarà il tag GCM a decidere lo stesso bivio.
        ChatEntry entry = ClearEnvelope.TryUnwrap(message.Nonce, message.Ciphertext, out string text)
            ? ChatEntry.User(message.Nickname, text, message.SentAt)
            : ChatEntry.Unverified(message.Nickname, message.SentAt);

        Add(entry);
    }

    internal void OnSystem(object? sender, SystemServerMessage message)
    {
        if (message.Kind == SystemKinds.RoomRenamed && message.RoomName is not null)
        {
            RoomName = message.RoomName;
        }

        Add(ChatEntry.FromSystem(message));
    }

    internal void OnClear(object? sender, EventArgs args) => Messages.Clear();

    internal void OnParticipants(object? sender, ParticipantsServerMessage message) =>
        ParticipantCount = message.Count;

    public void Dispose()
    {
        if (_Disposed)
        {
            return;
        }

        _Disposed = true;
        _Connection.MessageReceived -= OnMessage;
        _Connection.SystemReceived -= OnSystem;
        _Connection.ClearReceived -= OnClear;
        _Connection.ParticipantsChanged -= OnParticipants;
        _Connection.Disconnected -= OnDisconnected;
    }

    private void OnDisconnected(object? sender, KryptaDisconnectedEventArgs args)
    {
        // Niente resta in memoria, e non si lascia l'utente davanti a una chat morta.
        Dispose();
        Messages.Clear();
        HostScreen.Router.NavigateAndReset
            .Execute(new ConnectionViewModel(
                HostScreen,
                _Connection,
                new ConfigService()))
            .Subscribe();
    }

    private async Task LeaveAsync()
    {
        Dispose();
        Messages.Clear();

        try
        {
            await _Connection.LeaveAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ObjectDisposedException)
        {
            // Il socket è già andato: si torna comunque alla selezione stanza.
        }

        // Si torna a [3] con una schermata pulita. Quella da cui si è entrati sa già di
        // essere «entrata» e avrebbe i campi bloccati, quindi si toglie dalla pila.
        ObservableCollection<IRoutableViewModel> stack = HostScreen.Router.NavigationStack;
        while (stack.Count > 0 && stack[^1] is ChatViewModel or RoomViewModel)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        await HostScreen.Router.Navigate.Execute(new RoomViewModel(
            HostScreen,
            _Connection,
            _Session,
            MyNickname));
    }

    private void Add(ChatEntry entry) =>
        Messages.Add(ChatLine.For(entry, _Session.Theme));
}
