using Krypta.Client.Chat;
using Krypta.Client.Configuration;
using Krypta.Shared.Protocol;

namespace Krypta.Client.Tests;

/// <summary>
/// Punto 1A.4: il tipo unico dell'area messaggi e le sue tre varianti (05 §5.3).
/// </summary>
public sealed class ChatEntryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void User_UsaIlNicknameComeMittenteEIlColoreDellaPalette()
    {
        UserChatEntry entry = ChatEntry.User("luca", "ciao", Now);

        Assert.Equal("luca", entry.Sender);
        Assert.Equal(
            NicknameColors.ForNickname("luca", ClientTheme.Dark),
            entry.ColorFor(ClientTheme.Dark));
    }

    [Fact]
    public void System_UsaSempreMittenteStanzaEColoreFisso()
    {
        SystemChatEntry entry = ChatEntry.FromSystem(new SystemServerMessage(
            SystemKinds.UserJoined,
            "luca si è unito alla stanza, accettato da emy",
            Now));

        Assert.Equal("STANZA", entry.Sender);
        Assert.Equal(NicknameColors.ForSystem(ClientTheme.Dark), entry.ColorFor(ClientTheme.Dark));
        Assert.Equal(NicknameColors.ForSystem(ClientTheme.Light), entry.ColorFor(ClientTheme.Light));
    }

    [Fact]
    public void Local_UsaSempreMittenteKryptaEUnColoreDiversoDaStanza()
    {
        LocalChatEntry entry = ChatEntry.Local("Comando sconosciuto: /pippo.", Now);

        Assert.Equal("KRYPTA", entry.Sender);
        Assert.NotEqual(
            NicknameColors.ForSystem(ClientTheme.Dark),
            entry.ColorFor(ClientTheme.Dark));
    }

    [Fact]
    public void FromSystem_UsaIlTestoDelServerSenzaComporlo()
    {
        const string text = "emy2 ha rinominato la stanza in «Coordinamento»";
        SystemChatEntry entry = ChatEntry.FromSystem(new SystemServerMessage(
            SystemKinds.RoomRenamed,
            text,
            Now,
            RoomName: "Coordinamento"));

        Assert.Equal(text, entry.Text);
        Assert.Equal(SystemKinds.RoomRenamed, entry.Kind);
    }

    [Fact]
    public void FromSystem_KindSconosciuto_SiMostraLoStessoColSoloTesto()
    {
        SystemChatEntry entry = ChatEntry.FromSystem(new SystemServerMessage(
            "kind_che_non_esiste_ancora",
            "qualcosa è successo",
            Now));

        Assert.Equal("qualcosa è successo", entry.Text);
        Assert.Equal("kind_che_non_esiste_ancora", entry.Kind);
        Assert.Null(entry.Users);
    }

    [Fact]
    public void FromSystem_UserList_ConservaLElencoOltreAlTesto()
    {
        // Il client può renderlo come lista, ma il fallback sul solo `text` deve restare.
        SystemChatEntry entry = ChatEntry.FromSystem(new SystemServerMessage(
            SystemKinds.UserList,
            "Partecipanti (3/20): emy, luca, sara",
            Now,
            Users: ["emy", "luca", "sara"]));

        Assert.Equal(["emy", "luca", "sara"], entry.Users);
        Assert.Equal("Partecipanti (3/20): emy, luca, sara", entry.Text);
    }

    [Fact]
    public void Unverified_EUnaRigaLocaleCheNominaIlMittente()
    {
        LocalChatEntry entry = ChatEntry.Unverified("luca", Now);

        Assert.Equal("KRYPTA", entry.Sender);
        Assert.Contains("luca", entry.Text);
        Assert.Contains("non è stato verificato", entry.Text);
    }

    [Fact]
    public void TreVarianti_StannoNellaStessaCollezioneInOrdineCronologico()
    {
        // È il motivo per cui il tipo è unico: tre liste separate andrebbero fuse a mano.
        List<ChatEntry> messages =
        [
            ChatEntry.User("emy", "primo", Now),
            ChatEntry.FromSystem(new SystemServerMessage(
                SystemKinds.UserJoined, "luca si è unito alla stanza", Now.AddSeconds(1))),
            ChatEntry.Local("Comando sconosciuto: /pippo.", Now.AddSeconds(2)),
        ];

        Assert.Equal(["emy", "STANZA", "KRYPTA"], messages.Select(entry => entry.Sender));
        Assert.Equal(messages.OrderBy(entry => entry.At), messages);
    }
}
