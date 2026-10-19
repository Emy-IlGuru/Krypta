using System.Text.Json;
using Krypta.Server.Rooms;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

public sealed class MessageDispatcher(
    RoomManager roomManager,
    ProtocolMessageSender sender,
    ICommandHandler commandHandler,
    TimeProvider timeProvider)
{
    public async ValueTask DispatchAsync(
        Participant participant,
        string json,
        CancellationToken cancellationToken)
    {
        ClientMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(
                json,
                ProtocolJsonContext.Default.ClientMessage);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            sender.SendError(participant, ErrorCodes.Malformed);
            return;
        }

        if (message is null)
        {
            sender.SendError(participant, ErrorCodes.Malformed);
            return;
        }

        switch (message)
        {
            case JoinClientMessage join:
                DispatchJoin(participant, join);
                break;
            case ChatClientMessage chat:
                DispatchChat(participant, chat);
                break;
            case CommandClientMessage command:
                if (participant.CurrentRoom is null)
                {
                    sender.SendError(participant, ErrorCodes.NotInRoom);
                }
                else
                {
                    await commandHandler.HandleAsync(
                        participant,
                        command,
                        cancellationToken);
                }

                break;
            case ApproveClientMessage approve:
                DispatchApprove(participant, approve);
                break;
            case RejectClientMessage reject:
                DispatchReject(participant, reject);
                break;
            case CancelClientMessage:
                DispatchCancel(participant);
                break;
            case LeaveClientMessage:
                DispatchLeave(participant);
                break;
            case PongClientMessage:
                break;
            case RekeyClientMessage:
                sender.SendError(participant, ErrorCodes.InvalidRekey);
                break;
            default:
                sender.SendError(participant, ErrorCodes.UnknownType);
                break;
        }
    }

    public void Disconnect(Participant participant)
    {
        if (participant.IsPending)
        {
            Room? pendingRoom = participant.PendingRoom;
            OperationResult cancellation = roomManager.Cancel(participant);
            if (cancellation.Succeeded && pendingRoom is not null)
            {
                BroadcastWaiting(pendingRoom);
            }

            return;
        }

        Room? room = participant.CurrentRoom;
        if (room is null)
        {
            return;
        }

        LeaveResult result = roomManager.Leave(participant);
        if (!result.Succeeded)
        {
            return;
        }

        SendReleasedPending(room, result.ReleasedPending);
        if (!result.RoomDestroyed)
        {
            BroadcastRoomState(room);
        }
    }

    private void DispatchJoin(Participant participant, JoinClientMessage message)
    {
        JoinResult result = roomManager.Join(
            message.RoomId,
            message.Nickname,
            message.PublicKey,
            participant,
            message.JoinMessage,
            joined => ProtocolMessageSender.Serialize(new JoinedServerMessage(
                joined.Room!.Id,
                joined.Room.Name,
                joined.EffectiveNickname!,
                joined.Room.ParticipantCount,
                Created: true,
                KryptaLimits.MaxParticipantsPerRoom,
                joined.Room.KeyEpoch)));

        if (result.Outcome is JoinOutcome.Error)
        {
            sender.SendError(participant, result.ErrorCode!);
            return;
        }

        Room room = result.Room!;
        if (result.Outcome is JoinOutcome.Pending)
        {
            sender.Send(participant, new PendingServerMessage(
                room.Id,
                KryptaLimits.JoinApprovalTimeoutSeconds));
            BroadcastWaiting(room);
            return;
        }

        BroadcastRoomState(room);
    }

    private void DispatchChat(Participant participant, ChatClientMessage message)
    {
        Room? room = participant.CurrentRoom;
        if (room is null)
        {
            sender.SendError(participant, ErrorCodes.NotInRoom);
            return;
        }

        if (message.Seq < 1
            || message.KeyEpoch < 1
            || !ProtocolValueValidator.TryDecodeCanonicalBase64(
                message.Nonce,
                KryptaLimits.NonceBytes,
                out _)
            || !ProtocolValueValidator.TryDecodeCanonicalBase64(
                message.Ciphertext,
                out byte[] ciphertext)
            || ciphertext.Length <= KryptaLimits.GcmTagBytes)
        {
            sender.SendError(participant, ErrorCodes.InvalidEnvelope);
            return;
        }

        if (ciphertext.Length > KryptaLimits.MaxCiphertextBytes)
        {
            sender.SendError(participant, ErrorCodes.MessageTooLong);
            return;
        }

        sender.Broadcast(room, new ChatServerMessage(
            participant.Nickname!,
            message.Seq,
            message.KeyEpoch,
            message.Nonce,
            message.Ciphertext,
            timeProvider.GetUtcNow()));
    }

    private void DispatchApprove(Participant participant, ApproveClientMessage message)
    {
        ApprovalResult result = roomManager.Approve(
            participant,
            message.RequestId,
            message.WrappedKey,
            message.KeyEpoch,
            approved => ProtocolMessageSender.Serialize(new JoinedServerMessage(
                approved.Room!.Id,
                approved.Room.Name,
                approved.EffectiveNickname!,
                approved.Room.ParticipantCount + 1,
                Created: false,
                KryptaLimits.MaxParticipantsPerRoom,
                approved.Room.KeyEpoch,
                approved.WrappedKey)));

        if (result.Outcome is ApprovalOutcome.Error)
        {
            sender.SendError(participant, result.ErrorCode!);
            return;
        }

        Room room = result.Room!;
        Participant requester = result.Requester!;
        if (result.Outcome is ApprovalOutcome.Rejected)
        {
            sender.Send(requester, new RejectedServerMessage(
                room.Id,
                result.RejectionReason!));
            BroadcastWaiting(room);
            return;
        }

        BroadcastRoomState(room, includeWaiting: true);
    }

    private void DispatchReject(Participant participant, RejectClientMessage message)
    {
        ApprovalResult result = roomManager.Reject(participant, message.RequestId);
        if (result.Outcome is ApprovalOutcome.Error)
        {
            sender.SendError(participant, result.ErrorCode!);
            return;
        }

        sender.Send(result.Requester!, new RejectedServerMessage(
            result.Room!.Id,
            result.RejectionReason!));
        BroadcastWaiting(result.Room);
    }

    private void DispatchCancel(Participant participant)
    {
        Room? room = participant.PendingRoom;
        OperationResult result = roomManager.Cancel(participant);
        if (!result.Succeeded)
        {
            sender.SendError(participant, result.ErrorCode!);
            return;
        }

        if (room is not null)
        {
            BroadcastWaiting(room);
        }
    }

    private void DispatchLeave(Participant participant)
    {
        Room? room = participant.CurrentRoom;
        LeaveResult result = roomManager.Leave(participant);
        if (!result.Succeeded)
        {
            sender.SendError(participant, result.ErrorCode!);
            return;
        }

        sender.Send(participant, new LeftServerMessage(room!.Id));
        SendReleasedPending(room, result.ReleasedPending);

        if (!result.RoomDestroyed)
        {
            BroadcastRoomState(room);
        }
    }

    private void BroadcastRoomState(Room room, bool includeWaiting = false)
    {
        List<ServerMessage> messages =
        [
            new ParticipantsServerMessage(room.ParticipantCount),
            new RosterServerMessage(
                room.KeyEpoch,
                roomManager.GetRosterSnapshot(room))
        ];

        if (includeWaiting)
        {
            messages.Add(new WaitingServerMessage(
                roomManager.GetWaitingSnapshot(room)));
        }

        sender.Broadcast(room, messages.ToArray());
    }

    private void BroadcastWaiting(Room room) =>
        sender.Broadcast(room, new WaitingServerMessage(
            roomManager.GetWaitingSnapshot(room)));

    private void SendReleasedPending(
        Room room,
        IReadOnlyList<Participant> releasedPending)
    {
        foreach (Participant requester in releasedPending)
        {
            sender.Send(requester, new RejectedServerMessage(
                room.Id,
                RejectionReasons.RoomGone));
        }
    }
}
