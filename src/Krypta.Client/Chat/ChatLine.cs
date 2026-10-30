using Avalonia.Media;
using Krypta.Client.Configuration;

namespace Krypta.Client.Chat;

/// <summary>
/// Una riga pronta da disegnare: la voce più il pennello del mittente. Il colore dipende
/// dal <b>tema</b>, che non è una proprietà della voce, quindi non può stare dentro
/// <see cref="ChatEntry"/>: la coppia si costruisce qui, dove il tema si conosce.
/// </summary>
public sealed record ChatLine(ChatEntry Entry, IBrush SenderBrush)
{
    public static ChatLine For(ChatEntry entry, ClientTheme theme)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ChatLine(entry, new SolidColorBrush(entry.ColorFor(theme)));
    }

    public string Sender => Entry.Sender;

    public string Text => Entry.Text;

    /// <summary>Le righe di sistema e quelle locali vanno rese attenuate (05 §5.3).</summary>
    public bool IsDimmed => Entry is not UserChatEntry;
}
