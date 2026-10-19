using Krypta.Server.Rooms;

namespace Krypta.Server.Tests;

public sealed class NicknameAllocatorTests
{
    [Fact]
    public void Allocate_NomeLibero_RestituisceIlNomeRichiesto()
    {
        string actual = NicknameAllocator.Allocate("emy", []);

        Assert.Equal("emy", actual);
    }

    [Fact]
    public void Allocate_NomeGiaUsato_AggiungeIlPrimoSuffissoLibero()
    {
        string actual = NicknameAllocator.Allocate("emy", ["emy", "emy2"]);

        Assert.Equal("emy3", actual);
    }

    [Fact]
    public void Allocate_ConfrontoIgnoraMaiuscoleEMinuscole()
    {
        string actual = NicknameAllocator.Allocate("emy", ["Emy"]);

        Assert.Equal("emy2", actual);
    }

    [Fact]
    public void Allocate_NomeLungo_TroncaLaBasePerRestareEntroIlLimite()
    {
        const string requested = "abcdefghijklmnopqrstuvwxy";

        string actual = NicknameAllocator.Allocate(requested, [requested]);

        Assert.Equal("abcdefghijklmnopqrstuvwx2", actual);
        Assert.Equal(25, actual.Length);
    }

    [Theory]
    [InlineData(" emy ", "emy")]
    [InlineData("Emy_2.test-ok", "Emy_2.test-ok")]
    [InlineData("a b", "a b")]
    public void TryNormalize_NicknameValido_RestituisceLaFormaDopoIlTrim(
        string requested,
        string expected)
    {
        bool valid = NicknameAllocator.TryNormalize(requested, out string? normalized);

        Assert.True(valid);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("emy  due")]
    [InlineData("josé")]
    [InlineData("jose\u0301")]
    [InlineData("emoji🙂")]
    [InlineData("emy\t2")]
    [InlineData("STANZA")]
    [InlineData("krypta")]
    [InlineData("Sistema")]
    [InlineData("system")]
    [InlineData("SERVER")]
    [InlineData("room")]
    public void TryNormalize_NicknameNonValido_LoRifiuta(string requested)
    {
        bool valid = NicknameAllocator.TryNormalize(requested, out string? normalized);

        Assert.False(valid);
        Assert.Null(normalized);
    }
}
