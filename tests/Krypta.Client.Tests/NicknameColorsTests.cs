using Avalonia.Media;
using Krypta.Client.Chat;
using Krypta.Client.Configuration;

namespace Krypta.Client.Tests;

/// <summary>
/// Punto 1A.4: il colore del nickname (05 §6, ADR-0003). Deve uscire uguale su client
/// diversi, quindi i valori sono fissati qui come vettori, non solo confrontati con sé stessi.
/// </summary>
public sealed class NicknameColorsTests
{
    [Theory]
    [InlineData("", 2166136261u)]
    [InlineData("a", 0xE40C292Cu)]
    [InlineData("foobar", 0xBF9CF968u)]
    public void Fnv1a_VettoriNoti_Coincidono(string key, uint expected) =>
        Assert.Equal(expected, NicknameColors.Fnv1a(key));

    [Theory]
    [InlineData("emy", 14)]
    [InlineData("emy2", 16)]
    [InlineData("luca", 6)]
    [InlineData("sara", 12)]
    public void IndexFor_NicknameNoti_DannoIndiciFissi(string nickname, int expected) =>
        Assert.Equal(expected, NicknameColors.IndexFor(nickname));

    [Theory]
    [InlineData("Emy")]
    [InlineData("EMY")]
    [InlineData("  emy  ")]
    public void ForNickname_MaiuscoleESpazi_NonCambianoIlColore(string variant) =>
        Assert.Equal(
            NicknameColors.ForNickname("emy", ClientTheme.Dark),
            NicknameColors.ForNickname(variant, ClientTheme.Dark));

    [Fact]
    public void ForNickname_EmyEdEmy2_HannoColoriDiversi()
    {
        // La disambiguazione numerica del server si deve vedere anche dal colore.
        Assert.NotEqual(
            NicknameColors.ForNickname("emy", ClientTheme.Dark),
            NicknameColors.ForNickname("emy2", ClientTheme.Dark));
        Assert.NotEqual(
            NicknameColors.ForNickname("emy", ClientTheme.Light),
            NicknameColors.ForNickname("emy2", ClientTheme.Light));
    }

    [Fact]
    public void Palette_DueTemi_StessaLunghezzaVenti()
    {
        Assert.Equal(20, NicknameColors.PaletteSize);
        Assert.Equal(
            NicknameColors.Palette(ClientTheme.Dark).Count,
            NicknameColors.Palette(ClientTheme.Light).Count);
    }

    [Fact]
    public void Palette_LoStessoNickname_PrendeLoStessoIndiceNeiDueTemi()
    {
        int index = NicknameColors.IndexFor("luca");
        Assert.Equal(
            NicknameColors.Palette(ClientTheme.Dark)[index],
            NicknameColors.ForNickname("luca", ClientTheme.Dark));
        Assert.Equal(
            NicknameColors.Palette(ClientTheme.Light)[index],
            NicknameColors.ForNickname("luca", ClientTheme.Light));
    }

    [Theory]
    [InlineData(ClientTheme.Dark, "#E06C75", "#DADC60")]
    [InlineData(ClientTheme.Light, "#DA1B29", "#4C4D0A")]
    public void Palette_PrimoEUltimoColore_CoincidonoConLaTabellaDi05(
        ClientTheme theme,
        string first,
        string last)
    {
        IReadOnlyList<Color> palette = NicknameColors.Palette(theme);
        Assert.Equal(Color.Parse(first), palette[0]);
        Assert.Equal(Color.Parse(last), palette[^1]);
    }

    [Theory]
    [InlineData(ClientTheme.Dark)]
    [InlineData(ClientTheme.Light)]
    public void ForSystem_ELocal_NonSonoNellaPaletteDeiPartecipanti(ClientTheme theme)
    {
        IReadOnlyList<Color> palette = NicknameColors.Palette(theme);
        Assert.DoesNotContain(NicknameColors.ForSystem(theme), palette);
        Assert.DoesNotContain(NicknameColors.ForLocal(theme), palette);
        Assert.NotEqual(NicknameColors.ForSystem(theme), NicknameColors.ForLocal(theme));
    }

    [Fact]
    public void ForSystem_NonEIlColoreCheStanzaAvrebbeDallaPalette()
    {
        // "sara" e "stanza" cadono entrambi sull'indice 12: se STANZA passasse dalla
        // stessa funzione dei partecipanti, un messaggio di sistema sarebbe indistinguibile
        // da un messaggio di sara.
        Assert.Equal(
            NicknameColors.IndexFor("sara"),
            NicknameColors.IndexFor(NicknameColors.SystemSender));
        Assert.NotEqual(
            NicknameColors.ForNickname(NicknameColors.SystemSender, ClientTheme.Dark),
            NicknameColors.ForSystem(ClientTheme.Dark));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IndexFor_NicknameVuoto_RestaNellaPalette(string? nickname)
    {
        int index = NicknameColors.IndexFor(nickname);
        Assert.InRange(index, 0, NicknameColors.PaletteSize - 1);
    }
}
