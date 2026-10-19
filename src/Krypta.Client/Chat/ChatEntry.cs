using Avalonia.Media;
using Krypta.Client.Configuration;
using Krypta.Shared.Protocol;

namespace Krypta.Client.Chat;

/// <summary>
/// Riga dell'area messaggi. È un tipo <b>unico</b> con tre varianti — partecipante,
/// sistema, locale — così l'ordine cronologico è garantito da una sola collezione e non
/// da tre liste da fondere ([05 §5.3](../../../docs/05-client-desktop.md)).
/// </summary>
public abstract record ChatEntry(string Sender, string Text, DateTimeOffset At)
{
    /// <summary>
    /// Il colore lo decide la variante, non chi disegna: è ciò che impedisce a
    /// <c>STANZA</c> e <c>KRYPTA</c> di finire nella palette dei partecipanti.
    /// </summary>
    public abstract Color ColorFor(ClientTheme theme);

    public static UserChatEntry User(string nickname, string text, DateTimeOffset at) =>
        new(nickname, text, at);

    public static LocalChatEntry Local(string text, DateTimeOffset at) =>
        new(text, at);

    /// <summary>
    /// Il testo di un <c>system</c> arriva già pronto dal campo <c>text</c>: il client
    /// non lo compone mai, nemmeno per i <c>kind</c> che conosce.
    /// </summary>
    public static SystemChatEntry FromSystem(SystemServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new SystemChatEntry(message.Kind, message.Text, message.SentAt, message.Users);
    }

    /// <summary>
    /// Riga di un messaggio che non ha superato la verifica del tag o il controllo del
    /// <c>seq</c>. Non va mai mostrato come testo normale né scartato in silenzio: è
    /// l'unico segnale che l'utente ha di un server che riattribuisce o rigioca messaggi
    /// ([05 §5bis](../../../docs/05-client-desktop.md)).
    /// </summary>
    public static LocalChatEntry Unverified(string nickname, DateTimeOffset at) =>
        new($"Un messaggio da «{nickname}» non è stato verificato ed è stato scartato.", at);
}

/// <summary>Messaggio di un partecipante. Il mittente lo mette il server, mai il client.</summary>
public sealed record UserChatEntry(string Sender, string Text, DateTimeOffset At)
    : ChatEntry(Sender, Text, At)
{
    public override Color ColorFor(ClientTheme theme) =>
        NicknameColors.ForNickname(Sender, theme);
}

/// <summary>
/// Riga di sistema, mittente <c>STANZA</c>, visibile a tutta la stanza.
/// <paramref name="Kind"/> resta una stringa grezza: un sottotipo sconosciuto si mostra
/// comunque col solo <see cref="ChatEntry.Text"/>, e non è mai un errore.
/// </summary>
public sealed record SystemChatEntry(
    string Kind,
    string Text,
    DateTimeOffset At,
    IReadOnlyList<string>? Users = null)
    : ChatEntry(NicknameColors.SystemSender, Text, At)
{
    public override Color ColorFor(ClientTheme theme) =>
        NicknameColors.ForSystem(theme);
}

/// <summary>
/// Riga generata dal client — <c>/Help</c>, comando sconosciuto, argomento non valido —
/// mittente <c>KRYPTA</c>. <b>Non l'ha vista nessun altro:</b> senza la distinzione
/// visiva l'utente può credere di aver mandato in chat a tutti un errore che ha visto solo lui.
/// </summary>
public sealed record LocalChatEntry(string Text, DateTimeOffset At)
    : ChatEntry(NicknameColors.LocalSender, Text, At)
{
    public override Color ColorFor(ClientTheme theme) =>
        NicknameColors.ForLocal(theme);
}
