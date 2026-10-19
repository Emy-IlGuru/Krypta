using Krypta.Server.Connections;
using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;

namespace Krypta.Server.Tests;

public sealed class ParticipantQueueTests
{
    [Fact]
    public async Task RunWriterAsync_InviiConcorrenti_ScriveSulSocketUnoAllaVolta()
    {
        FakeClientConnection connection = new()
        {
            SendDelay = TimeSpan.FromMilliseconds(1)
        };
        using Participant participant = new(connection);
        Task writer = participant.RunWriterAsync(CancellationToken.None);

        await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(index => Task.Run(() => participant.TryEnqueue(index.ToString()))));
        participant.CompleteOutgoing();
        await writer;

        Assert.Equal(50, connection.Sent.Count);
        Assert.Equal(1, connection.MaximumConcurrentSends);
    }

    [Fact]
    public void Enqueue_CodaOltreIlLimite_RichiedeLaDisconnessione()
    {
        using Participant participant = new(new FakeClientConnection());

        for (int index = 0; index < KryptaLimits.SendQueueCapacity; index++)
        {
            Assert.True(participant.TryEnqueue(index.ToString()));
        }

        Assert.False(participant.TryEnqueue("overflow"));
        Assert.True(participant.DisconnectRequested);
        Assert.Equal(DisconnectReason.SlowClient, participant.DisconnectReason);
    }
}
