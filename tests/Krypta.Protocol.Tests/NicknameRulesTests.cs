using Krypta.Shared;

namespace Krypta.Protocol.Tests;

/// <summary>
/// Le regole del nickname stanno nel contratto condiviso: se client e server ne tenessero
/// due copie, esisterebbe un nickname che il campo accetta e il `join` rifiuta.
/// </summary>
public sealed class NicknameRulesTests
{
    [Theory]
    [InlineData("emy")]
    [InlineData("mario.rossi")]
    [InlineData("anna maria")]
    [InlineData("a-b_c")]
    [InlineData("012")]
    public void Validate_NicknameAmmessi_NessunProblema(string nickname) =>
        Assert.Equal(NicknameProblem.None, NicknameRules.Validate(nickname));

    [Theory]
    [InlineData(null, NicknameProblem.Empty)]
    [InlineData("", NicknameProblem.Empty)]
    [InlineData("   ", NicknameProblem.Empty)]
    [InlineData("ab", NicknameProblem.TooShort)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaa", NicknameProblem.TooLong)]
    [InlineData("STANZA", NicknameProblem.Reserved)]
    [InlineData("krypta", NicknameProblem.Reserved)]
    [InlineData("Server", NicknameProblem.Reserved)]
    [InlineData("émy", NicknameProblem.InvalidCharacters)]
    [InlineData("emy😀", NicknameProblem.InvalidCharacters)]
    [InlineData("a  b", NicknameProblem.InvalidCharacters)]
    [InlineData("emy!", NicknameProblem.InvalidCharacters)]
    public void Validate_NicknameRifiutati_DiconoPerche(
        string? nickname,
        NicknameProblem expected) =>
        Assert.Equal(expected, NicknameRules.Validate(nickname));

    [Fact]
    public void TryNormalize_ApplicaSoloIlTrim()
    {
        // Non tocca le maiuscole: `Emy` ed `emy` restano nickname distinti, e il server
        // li disambigua fra loro solo perché il confronto di occupazione è case-insensitive.
        Assert.True(NicknameRules.TryNormalize("  Emy  ", out string? normalized));
        Assert.Equal("Emy", normalized);
    }

    [Fact]
    public void TryNormalize_NicknameRifiutato_NonRestituisceNiente()
    {
        Assert.False(NicknameRules.TryNormalize("ab", out string? normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void ReservedNames_SonoQuelliCheIlServerRifiuta() =>
        Assert.Equal(
            ["STANZA", "KRYPTA", "SISTEMA", "SYSTEM", "SERVER", "ROOM"],
            NicknameRules.ReservedNames);
}
