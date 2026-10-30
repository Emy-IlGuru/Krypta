using Krypta.Client.Rooms;
using Krypta.Shared;

namespace Krypta.Client.Tests;

public sealed class RoomIdGeneratorTests
{
    [Fact]
    public void Next_ProduceSempreUnIdAccettatoDalServer()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string id = RoomIdGenerator.Next();

            Assert.Equal(KryptaLimits.RoomIdLength, id.Length);
            Assert.All(id, character =>
                Assert.True(character is >= 'A' and <= 'Z' or >= '0' and <= '9'));
        }
    }

    [Fact]
    public void Next_NonRipeteLoStessoIdNellImmediato()
    {
        // Non è un test di casualità — quello non si fa così — ma coglie il caso in cui
        // il generatore restituisse una costante o partisse da un seme fisso.
        HashSet<string> generated = [];
        for (int attempt = 0; attempt < 100; attempt++)
        {
            generated.Add(RoomIdGenerator.Next());
        }

        Assert.True(generated.Count > 90, $"Solo {generated.Count} ID distinti su 100.");
    }
}
