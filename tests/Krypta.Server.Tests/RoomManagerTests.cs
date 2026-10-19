using Krypta.Server.Rooms;
using Krypta.Server.Tests.TestDoubles;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests;

public sealed class RoomManagerTests
{
    [Fact]
    public void Join_StanzaInesistente_LaCreaEAmmetteIlPrimoPartecipante()
    {
        RoomManager manager = new();
        Participant participant = CreateParticipant();

        JoinResult result = Join(manager, "K7X2M9", " emy ", participant);

        Assert.Equal(JoinOutcome.Joined, result.Outcome);
        Assert.True(result.Created);
        Assert.Equal("emy", result.EffectiveNickname);
        Assert.NotNull(result.Room);
        Assert.Equal("RM-K7X2M9", result.Room.Name);
        Assert.Equal(1, result.Room.KeyEpoch);
        Assert.Equal(1, result.Room.ParticipantCount);
        Assert.Same(result.Room, participant.CurrentRoom);
        Assert.False(participant.IsPending);
        Assert.Equal(1, manager.RoomCount);
    }

    [Fact]
    public void Join_StanzaEsistente_MetteIlRichiedenteInAttesa()
    {
        RoomManager manager = new();
        Join(manager, "K7X2M9", "emy", CreateParticipant());
        Participant requester = CreateParticipant();

        JoinResult result = Join(
            manager,
            "K7X2M9",
            "luca",
            requester,
            "  Sono Luca  ");

        Assert.Equal(JoinOutcome.Pending, result.Outcome);
        Assert.NotNull(result.RequestId);
        Assert.Null(requester.CurrentRoom);
        Assert.True(requester.IsPending);
        Assert.Equal(1, result.Room!.ParticipantCount);
        Assert.Equal(1, result.Room.PendingCount);
        JoinRequest request = Assert.Single(result.Room.Pending);
        Assert.Equal("luca", request.RequestedNickname);
        Assert.Equal("Sono Luca", request.JoinMessage);
    }

    [Fact]
    public void Join_StanzaPiena_RestituisceRoomFullSenzaAccodare()
    {
        RoomManager manager = new();
        Participant approver = CreateParticipant();
        Join(manager, "K7X2M9", "user1", approver);

        for (int index = 2; index <= KryptaLimits.MaxParticipantsPerRoom; index++)
        {
            Participant requester = CreateParticipant();
            JoinResult pending = Join(manager, "K7X2M9", $"user{index}", requester);
            ApprovalResult approved = manager.Approve(
                approver,
                pending.RequestId!.Value,
                ValidWrappedKey(),
                keyEpoch: 1);

            Assert.Equal(ApprovalOutcome.Approved, approved.Outcome);
        }

        Participant overflow = CreateParticipant();
        JoinResult result = Join(manager, "K7X2M9", "overflow", overflow);

        Assert.Equal(JoinOutcome.Error, result.Outcome);
        Assert.Equal(ErrorCodes.RoomFull, result.ErrorCode);
        Assert.Null(overflow.CurrentRoom);
        Assert.False(overflow.IsPending);
        Assert.Equal(0, result.Room!.PendingCount);
    }

    [Fact]
    public async Task Join_ConcorrenteStessoId_CreaUnaSolaStanza()
    {
        RoomManager manager = new();
        Participant[] participants = Enumerable.Range(0, 50)
            .Select(_ => CreateParticipant())
            .ToArray();
        using ManualResetEventSlim start = new(false);

        Task<JoinResult>[] joins = participants.Select((participant, index) => Task.Run(() =>
        {
            start.Wait();
            return Join(manager, "K7X2M9", $"user{index}", participant);
        })).ToArray();

        start.Set();
        JoinResult[] results = await Task.WhenAll(joins);

        Assert.Equal(1, manager.RoomCount);
        Assert.Equal(1, results.Count(result => result.Outcome == JoinOutcome.Joined));
        Assert.Equal(KryptaLimits.MaxPendingPerRoom,
            results.Count(result => result.Outcome == JoinOutcome.Pending));
        Assert.All(results.Where(result => result.Outcome == JoinOutcome.Error),
            result => Assert.Equal(ErrorCodes.WaitingRoomFull, result.ErrorCode));
        Room room = Assert.Single(results.Where(result => result.Room is not null)
            .Select(result => result.Room!)
            .Distinct());
        Assert.Equal(1, room.ParticipantCount);
        Assert.Equal(KryptaLimits.MaxPendingPerRoom, room.PendingCount);
    }

    [Fact]
    public void Leave_UltimoPartecipante_DistruggeStanzaELiberaLeRichieste()
    {
        RoomManager manager = new();
        Participant owner = CreateParticipant();
        Room originalRoom = Join(manager, "K7X2M9", "emy", owner).Room!;
        Participant requester = CreateParticipant();
        Join(manager, "K7X2M9", "luca", requester);

        LeaveResult leave = manager.Leave(owner);

        Assert.True(leave.Succeeded);
        Assert.True(leave.RoomDestroyed);
        Assert.Same(requester, Assert.Single(leave.ReleasedPending));
        Assert.Null(owner.CurrentRoom);
        Assert.False(requester.IsPending);
        Assert.Equal(0, manager.RoomCount);

        JoinResult recreated = Join(manager, "K7X2M9", "sara", CreateParticipant());
        Assert.True(recreated.Created);
        Assert.NotSame(originalRoom, recreated.Room);
        Assert.Equal("RM-K7X2M9", recreated.Room!.Name);
    }

    [Fact]
    public async Task Approve_ConcorrenteStessaRichiesta_LaEvadeUnaSolaVolta()
    {
        RoomManager manager = new();
        Participant owner = CreateParticipant();
        Join(manager, "K7X2M9", "owner", owner);
        Participant secondApprover = await JoinAndApproveAsync(manager, owner, "second");
        JoinResult pending = Join(manager, "K7X2M9", "luca", CreateParticipant());
        using Barrier barrier = new(2);

        Participant[] approvers = [owner, secondApprover];
        Task<ApprovalResult>[] approvals = approvers
            .Select(approver => Task.Run(() =>
            {
                barrier.SignalAndWait();
                return manager.Approve(
                    approver,
                    pending.RequestId!.Value,
                    ValidWrappedKey(),
                    keyEpoch: 1);
            }))
            .ToArray();

        ApprovalResult[] results = await Task.WhenAll(approvals);

        Assert.Equal(1, results.Count(result => result.Outcome == ApprovalOutcome.Approved));
        ApprovalResult error = Assert.Single(results,
            result => result.Outcome == ApprovalOutcome.Error);
        Assert.Equal(ErrorCodes.UnknownRequest, error.ErrorCode);
        Assert.Equal(3, pending.Room!.ParticipantCount);
        Assert.Equal(0, pending.Room.PendingCount);
    }

    [Fact]
    public async Task Approve_ConcorrenteNicknameUguale_AssegnaNomiUnivoci()
    {
        RoomManager manager = new();
        Participant owner = CreateParticipant();
        Join(manager, "K7X2M9", "owner", owner);
        JoinResult[] pending = Enumerable.Range(0, KryptaLimits.MaxPendingPerRoom)
            .Select(_ => Join(manager, "K7X2M9", "emy", CreateParticipant()))
            .ToArray();
        using Barrier barrier = new(pending.Length);

        Task<ApprovalResult>[] approvals = pending.Select(result => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return manager.Approve(
                owner,
                result.RequestId!.Value,
                ValidWrappedKey(),
                keyEpoch: 1);
        })).ToArray();

        ApprovalResult[] results = await Task.WhenAll(approvals);
        string[] nicknames = results.Select(result => result.EffectiveNickname!).ToArray();

        Assert.All(results, result => Assert.Equal(ApprovalOutcome.Approved, result.Outcome));
        Assert.Equal(nicknames.Length,
            nicknames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("emy", nicknames);
        Assert.Contains("emy10", nicknames);
    }

    [Fact]
    public void Join_PartecipanteGiaDentro_RestituisceAlreadyInRoom()
    {
        RoomManager manager = new();
        Participant participant = CreateParticipant();
        Join(manager, "K7X2M9", "emy", participant);

        JoinResult result = Join(manager, "ABC123", "emy", participant);

        Assert.Equal(JoinOutcome.Error, result.Outcome);
        Assert.Equal(ErrorCodes.AlreadyInRoom, result.ErrorCode);
        Assert.Equal(1, manager.RoomCount);
    }

    [Fact]
    public void Join_PartecipanteGiaInAttesa_RestituisceAlreadyPending()
    {
        RoomManager manager = new();
        Join(manager, "K7X2M9", "owner", CreateParticipant());
        Participant requester = CreateParticipant();
        Join(manager, "K7X2M9", "emy", requester);

        JoinResult result = Join(manager, "ABC123", "emy", requester);

        Assert.Equal(JoinOutcome.Error, result.Outcome);
        Assert.Equal(ErrorCodes.AlreadyPending, result.ErrorCode);
        Assert.Equal(1, manager.RoomCount);
    }

    [Theory]
    [InlineData("k7x2m9")]
    [InlineData(" K7X2M9")]
    [InlineData("K7X2M9 ")]
    [InlineData("K7X2M")]
    [InlineData("K7X2M99")]
    [InlineData("K7X-M9")]
    [InlineData("K7XéM9")]
    public void Join_IdStanzaNonValido_LoRifiutaSenzaNormalizzarlo(string roomId)
    {
        RoomManager manager = new();

        JoinResult result = Join(manager, roomId, "emy", CreateParticipant());

        Assert.Equal(JoinOutcome.Error, result.Outcome);
        Assert.Equal(ErrorCodes.InvalidRoomId, result.ErrorCode);
        Assert.Equal(0, manager.RoomCount);
    }

    [Fact]
    public void Approve_InvolucroNonValido_NonAmmetteIlRichiedente()
    {
        RoomManager manager = new();
        Participant owner = CreateParticipant();
        Join(manager, "K7X2M9", "owner", owner);
        JoinResult pending = Join(manager, "K7X2M9", "emy", CreateParticipant());

        ApprovalResult result = manager.Approve(
            owner,
            pending.RequestId!.Value,
            new WrappedKey("invalid", "invalid", "invalid"),
            keyEpoch: 1);

        Assert.Equal(ApprovalOutcome.Error, result.Outcome);
        Assert.Equal(ErrorCodes.InvalidApproval, result.ErrorCode);
        Assert.Equal(1, pending.Room!.PendingCount);
        Assert.Equal(1, pending.Room.ParticipantCount);
    }

    private static async Task<Participant> JoinAndApproveAsync(
        RoomManager manager,
        Participant approver,
        string nickname)
    {
        Participant participant = CreateParticipant();
        JoinResult pending = Join(manager, "K7X2M9", nickname, participant);
        ApprovalResult approved = await Task.Run(() => manager.Approve(
            approver,
            pending.RequestId!.Value,
            ValidWrappedKey(),
            keyEpoch: 1));
        Assert.Equal(ApprovalOutcome.Approved, approved.Outcome);
        return participant;
    }

    private static Participant CreateParticipant()
    {
        return new Participant(new FakeClientConnection());
    }

    private static JoinResult Join(
        RoomManager manager,
        string roomId,
        string nickname,
        Participant participant,
        string? joinMessage = null)
    {
        return manager.Join(
            roomId,
            nickname,
            ValidPublicKey(),
            participant,
            joinMessage);
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
