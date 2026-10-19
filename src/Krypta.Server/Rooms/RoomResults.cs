using Krypta.Shared.Protocol;

namespace Krypta.Server.Rooms;

public enum JoinOutcome
{
    Joined,
    Pending,
    Error
}

public sealed record JoinResult(
    JoinOutcome Outcome,
    Room? Room = null,
    bool Created = false,
    string? EffectiveNickname = null,
    Guid? RequestId = null,
    string? ErrorCode = null)
{
    internal static JoinResult Joined(Room room, string nickname) =>
        new(JoinOutcome.Joined, room, Created: true, EffectiveNickname: nickname);

    internal static JoinResult Pending(Room room, Guid requestId) =>
        new(JoinOutcome.Pending, room, RequestId: requestId);

    internal static JoinResult Error(string errorCode, Room? room = null) =>
        new(JoinOutcome.Error, room, ErrorCode: errorCode);
}

public enum ApprovalOutcome
{
    Approved,
    Rejected,
    Error
}

public sealed record ApprovalResult(
    ApprovalOutcome Outcome,
    Room? Room = null,
    Participant? Requester = null,
    string? EffectiveNickname = null,
    WrappedKey? WrappedKey = null,
    string? RejectionReason = null,
    string? ErrorCode = null)
{
    internal static ApprovalResult Approved(
        Room room,
        Participant requester,
        string nickname,
        WrappedKey wrappedKey) =>
        new(
            ApprovalOutcome.Approved,
            room,
            requester,
            nickname,
            wrappedKey);

    internal static ApprovalResult Rejected(
        Room room,
        Participant requester,
        string reason) =>
        new(
            ApprovalOutcome.Rejected,
            room,
            requester,
            RejectionReason: reason);

    internal static ApprovalResult Error(string errorCode, Room? room = null) =>
        new(ApprovalOutcome.Error, room, ErrorCode: errorCode);
}

public sealed record OperationResult(bool Succeeded, string? ErrorCode = null)
{
    internal static OperationResult Success() => new(true);

    internal static OperationResult Error(string errorCode) => new(false, errorCode);
}

public sealed record LeaveResult(
    bool Succeeded,
    bool RoomDestroyed = false,
    IReadOnlyList<Participant>? PendingReleased = null,
    string? ErrorCode = null)
{
    public IReadOnlyList<Participant> ReleasedPending { get; } =
        PendingReleased ?? Array.Empty<Participant>();

    internal static LeaveResult Left(bool roomDestroyed, IReadOnlyList<Participant> released) =>
        new(true, roomDestroyed, released);

    internal static LeaveResult Error(string errorCode) =>
        new(false, ErrorCode: errorCode);
}
