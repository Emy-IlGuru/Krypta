using System.Text.Json;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

public sealed class MessageDispatcher(
    RoomManager roomManager,
    ProtocolMessageSender sender,
    ICommandHandler commandHandler,
    ActionRateLimiter actionRateLimiter,
    JoinRateLimiter joinRateLimiter,
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
                // Il controllo sta qui e non all'apertura della connessione: conta ogni
                // tentativo, quindi leave+join ripetuti non aggirano il limite (04 §4.6).
                if (!joinRateLimiter.TryJoin(
                    participant.RemoteAddress,
                    out int joinRetryAfter))
                {
                    sender.SendError(
                        participant,
                        ErrorCodes.JoinRateLimited,
                        joinRetryAfter);
                    break;
                }

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
                else if (!actionRateLimiter.TryCommand(
                    participant,
                    out int commandRetryAfter))
                {
                    sender.SendError(
                        participant,
                        ErrorCodes.CommandRateLimited,
                        commandRetryAfter);
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

        // Il nickname va letto PRIMA di Leave: uscendo la connessione lo perde,
        // e senza non si può più dire a chi si riferisce l'annuncio.
        string nickname = participant.Nickname!;

        LeaveResult result = roomManager.Leave(participant);
        if (!result.Succeeded)
        {
            return;
        }

        SendReleasedPending(room, result.ReleasedPending);
        if (!result.RoomDestroyed)
        {
            BroadcastRoomState(
                room,
                SystemMessages.UserLeft(nickname, timeProvider.GetUtcNow()));
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
            BroadcastWaiting(
                room,
                SystemMessages.JoinRequested(
                    result.RequestedNickname!,
                    timeProvider.GetUtcNow()));
            return;
        }

        BroadcastRoomState(
            room,
            SystemMessages.RoomCreated(
                result.EffectiveNickname!,
                timeProvider.GetUtcNow()));
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

        if (!actionRateLimiter.TryMessage(participant))
        {
            sender.SendError(participant, ErrorCodes.RateLimited);
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
            BroadcastWaiting(
                room,
                SystemMessages.JoinRejected(
                    participant.Nickname!,
                    result.RequestedNickname!,
                    timeProvider.GetUtcNow()));
            return;
        }

        BroadcastRoomState(
            room,
            SystemMessages.UserJoined(
                result.EffectiveNickname!,
                participant.Nickname!,
                timeProvider.GetUtcNow()),
            includeWaiting: true);
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
        BroadcastWaiting(
            result.Room,
            SystemMessages.JoinRejected(
                participant.Nickname!,
                result.RequestedNickname!,
                timeProvider.GetUtcNow()));
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
        string? nickname = participant.Nickname;

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
            BroadcastRoomState(
                room,
                SystemMessages.UserLeft(nickname!, timeProvider.GetUtcNow()));
        }
    }

    /// <summary>
    /// L'ordine è vincolante e segue 03 §10: prima la coda d'attesa aggiornata,
    /// poi la riga di sistema che spiega cos'è successo, poi il nuovo conteggio.
    /// </summary>
    private void BroadcastRoomState(
        Room room,
        SystemServerMessage? announcement = null,
        bool includeWaiting = false)
    {
        List<ServerMessage> messages = [];

        if (includeWaiting)
        {
            messages.Add(new WaitingServerMessage(
                roomManager.GetWaitingSnapshot(room)));
        }

        if (announcement is not null)
        {
            messages.Add(announcement);
        }

        messages.Add(new ParticipantsServerMessage(room.ParticipantCount));
        messages.Add(new RosterServerMessage(
            room.KeyEpoch,
            roomManager.GetRosterSnapshot(room)));

        sender.Broadcast(room, messages.ToArray());
    }

    private void BroadcastWaiting(Room room, SystemServerMessage? announcement = null)
    {
        List<ServerMessage> messages =
        [
            new WaitingServerMessage(roomManager.GetWaitingSnapshot(room))
        ];

        if (announcement is not null)
        {
            messages.Add(announcement);
        }

        sender.Broadcast(room, messages.ToArray());
    }

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
