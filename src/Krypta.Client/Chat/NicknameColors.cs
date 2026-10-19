using System.Text;
using Avalonia.Media;
using Krypta.Client.Configuration;

namespace Krypta.Client.Chat;

/// <summary>
/// Colore deterministico del nickname: stesso nome, stesso colore su ogni client, senza
/// alcun coordinamento col server ([05 §6](../../../docs/05-client-desktop.md)).
/// </summary>
public static class NicknameColors
{
    /// <summary>Mittente dei messaggi di sistema. Nome riservato lato server.</summary>
    public const string SystemSender = "STANZA";

    /// <summary>Mittente dei messaggi locali, quelli che ha visto solo chi li ha causati.</summary>
    public const string LocalSender = "KRYPTA";

    private const uint Fnv1aOffsetBasis = 2166136261u;
    private const uint Fnv1aPrime = 16777619u;

    // Contrasti misurati su #0E1116, tutti sopra il 4.5:1 di WCAG AA (RNF-10).
    private static readonly Color[] DarkPalette =
    [
        Color.Parse("#E06C75"), Color.Parse("#98C379"), Color.Parse("#E6A4B4"),
        Color.Parse("#82AAFF"), Color.Parse("#61AFEF"), Color.Parse("#C678DD"),
        Color.Parse("#89DDFF"), Color.Parse("#C3E88D"), Color.Parse("#E5C07B"),
        Color.Parse("#D19A66"), Color.Parse("#B5CEA8"), Color.Parse("#FFCB6B"),
        Color.Parse("#56B6C2"), Color.Parse("#7FD1B9"), Color.Parse("#F78C6C"),
        Color.Parse("#DDBBAA"), Color.Parse("#B0A4DA"), Color.Parse("#60DC75"),
        Color.Parse("#CB80B8"), Color.Parse("#DADC60"),
    ];

    // Stessi indici, tinte diverse: non è la palette scura schiarita, ogni tinta conserva
    // l'angolo di colore ma viene saturata e scurita fino a superare AA sul bianco.
    private static readonly Color[] LightPalette =
    [
        Color.Parse("#DA1B29"), Color.Parse("#3E5F17"), Color.Parse("#C9264D"),
        Color.Parse("#0A53EE"), Color.Parse("#065290"), Color.Parse("#AD23D6"),
        Color.Parse("#054E6C"), Color.Parse("#32680C"), Color.Parse("#614A0A"),
        Color.Parse("#A74C24"), Color.Parse("#1D4A1C"), Color.Parse("#8C6D06"),
        Color.Parse("#19535A"), Color.Parse("#1F7159"), Color.Parse("#D03609"),
        Color.Parse("#7F4D18"), Color.Parse("#6240DA"), Color.Parse("#098144"),
        Color.Parse("#BA2594"), Color.Parse("#4C4D0A"),
    ];

    private static readonly Color DarkSystem = Color.Parse("#6E7883");
    private static readonly Color LightSystem = Color.Parse("#6B7684");
    private static readonly Color DarkLocal = Color.Parse("#A79BFF");
    private static readonly Color LightLocal = Color.Parse("#4A3FBF");

    /// <summary>Le due palette hanno la stessa lunghezza: l'indice non dipende dal tema.</summary>
    public static int PaletteSize => DarkPalette.Length;

    public static IReadOnlyList<Color> Palette(ClientTheme theme) =>
        theme is ClientTheme.Light ? LightPalette : DarkPalette;

    /// <summary>
    /// Va chiamata sul nickname <b>effettivo</b>, quello restituito dal server: <c>emy</c>
    /// ed <c>emy2</c> sono nomi diversi e prendono colori diversi, così la disambiguazione
    /// si vede anche dal colore.
    /// </summary>
    public static Color ForNickname(string? nickname, ClientTheme theme) =>
        Palette(theme)[IndexFor(nickname)];

    public static int IndexFor(string? nickname) =>
        (int)(Fnv1a(Normalize(nickname)) % (uint)DarkPalette.Length);

    /// <summary>
    /// Grigio fisso di <c>STANZA</c>. Non passa dalla palette di proposito: <c>sara</c>
    /// e <c>stanza</c> cadono sullo stesso indice, e un messaggio di sistema dello stesso
    /// colore di un partecipante sarebbe indistinguibile da un messaggio vero.
    /// </summary>
    public static Color ForSystem(ClientTheme theme) =>
        theme is ClientTheme.Light ? LightSystem : DarkSystem;

    /// <summary>Accento fisso di <c>KRYPTA</c>, distinto da quello di <c>STANZA</c>.</summary>
    public static Color ForLocal(ClientTheme theme) =>
        theme is ClientTheme.Light ? LightLocal : DarkLocal;

    /// <summary>
    /// FNV-1a a 32 bit sui byte UTF-8. <b>Mai</b> <c>string.GetHashCode()</c>: in .NET è
    /// randomizzato a ogni avvio del processo, quindi darebbe colori diversi su client
    /// diversi ([ADR-0003](../../../docs/adr/0003-colori-nickname-deterministici.md)).
    /// </summary>
    internal static uint Fnv1a(string key)
    {
        uint hash = Fnv1aOffsetBasis;
        foreach (byte value in Encoding.UTF8.GetBytes(key))
        {
            hash ^= value;
            hash *= Fnv1aPrime;
        }

        return hash;
    }

    /// <summary>«Emy», «emy» e « emy » devono dare lo stesso colore.</summary>
    private static string Normalize(string? nickname) =>
        (nickname ?? string.Empty).Trim().ToLowerInvariant();
}
