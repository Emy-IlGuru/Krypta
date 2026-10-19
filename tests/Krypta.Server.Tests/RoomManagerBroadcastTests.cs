using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests;

public sealed class RoomManagerBroadcastTests
{
    [Fact]
    public async Task Broadcast_Concorrente_MantieneLoStessoOrdineInTutteLeCode()
    {
        RoomManager manager = new();
        (Participant Participant, FakeClientConnection Connection)[] clients =
            Enumerable.Range(0, KryptaLimits.MaxParticipantsPerRoom)
                .Select(_ => CreateClient())
                .ToArray();
        Participant owner = clients[0].Participant;
        Room room = manager.Join(
            "K7X2M9",
            "user0",
            ValidPublicKey(),
            owner).Room!;

        for (int index = 1; index < clients.Length; index++)
        {
            JoinResult pending = manager.Join(
                "K7X2M9",
                $"user{index}",
                ValidPublicKey(),
                clients[index].Participant);
            manager.Approve(owner, pending.RequestId!.Value, ValidWrappedKey(), 1);
        }

        Task[] writers = clients
            .Select(client => client.Participant.RunWriterAsync(CancellationToken.None))
            .ToArray();
        using Barrier barrier = new(2);
        Task first = Task.Run(() =>
        {
            barrier.SignalAndWait();
            manager.Broadcast(room, ["first"]);
        });
        Task second = Task.Run(() =>
        {
            barrier.SignalAndWait();
            manager.Broadcast(room, ["second"]);
        });

        await Task.WhenAll(first, second);
        foreach ((Participant participant, _) in clients)
        {
            participant.CompleteOutgoing();
        }

        await Task.WhenAll(writers);

        string[] expected = clients[0].Connection.Sent.ToArray();
        Assert.Equal(2, expected.Length);
        Assert.All(clients, client => Assert.Equal(expected, client.Connection.Sent));
        Assert.All(clients, client => client.Participant.Dispose());
    }

    [Fact]
    public async Task Broadcast_RaggiungeMittenteEStanzaMaNonLeAltreStanze()
    {
        RoomManager manager = new();
        (Participant first, FakeClientConnection firstConnection) = CreateClient();
        (Participant second, FakeClientConnection secondConnection) = CreateClient();
        Room firstRoom = manager.Join(
            "K7X2M9",
            "first",
            ValidPublicKey(),
            first).Room!;
        manager.Join("ABC123", "second", ValidPublicKey(), second);
        Task firstWriter = first.RunWriterAsync(CancellationToken.None);
        Task secondWriter = second.RunWriterAsync(CancellationToken.None);

        manager.Broadcast(firstRoom, ["payload"]);
        first.CompleteOutgoing();
        second.CompleteOutgoing();
        await Task.WhenAll(firstWriter, secondWriter);

        Assert.Equal(["payload"], firstConnection.Sent);
        Assert.Empty(secondConnection.Sent);
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task Approve_ConBroadcastConcorrente_AccodaJoinedPrimaDelMessaggio()
    {
        RoomManager manager = new();
        (Participant owner, _) = CreateClient();
        (Participant requester, FakeClientConnection requesterConnection) = CreateClient();
        Room room = manager.Join(
            "K7X2M9",
            "owner",
            ValidPublicKey(),
            owner).Room!;
        JoinResult pending = manager.Join(
            "K7X2M9",
            "requester",
            ValidPublicKey(),
            requester);
        using ManualResetEventSlim factoryEntered = new(false);
        using ManualResetEventSlim releaseFactory = new(false);
        Task writer = requester.RunWriterAsync(CancellationToken.None);

        Task approval = Task.Run(() => manager.Approve(
            owner,
            pending.RequestId!.Value,
            ValidWrappedKey(),
            1,
            _ =>
            {
                factoryEntered.Set();
                releaseFactory.Wait();
                return "joined";
            }));
        factoryEntered.Wait();
        Task broadcast = Task.Run(() => manager.Broadcast(room, ["message"]));
        releaseFactory.Set();

        await Task.WhenAll(approval, broadcast);
        requester.CompleteOutgoing();
        await writer;

        Assert.Equal(["joined", "message"], requesterConnection.Sent);
        owner.Dispose();
        requester.Dispose();
    }

    private static (Participant Participant, FakeClientConnection Connection) CreateClient()
    {
        FakeClientConnection connection = new();
        return (new Participant(connection), connection);
    }

    private static string ValidPublicKey()
    {
        byte[] publicKey = new byte[KryptaLimits.PublicKeyBytes];
        publicKey[0] = 0x04;
        return Convert.ToBase64String(publicKey);
    }

    private static WrappedKey ValidWrappedKey()
    {
        byte[] epk = new byte[KryptaLimits.PublicKeyBytes];
        epk[0] = 0x04;
        return new WrappedKey(
            Convert.ToBase64String(epk),
            Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]),
            Convert.ToBase64String(new byte[KryptaLimits.WrappedKeyCiphertextBytes]));
    }
}
