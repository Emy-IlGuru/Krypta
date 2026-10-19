using Krypta.Shared;
using Krypta.Shared.Protocol;
using Krypta.Server.Messaging;

namespace Krypta.Server.Rooms;

/// <summary>
/// Unico proprietario dello stato effimero delle stanze.
/// </summary>
public sealed class RoomManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public RoomManager(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int RoomCount
    {
        get
        {
            lock (_gate)
            {
                return _rooms.Count;
            }
        }
    }

    public bool TryGetRoom(string roomId, out Room? room)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(roomId, out room);
        }
    }

    /// <summary>
    /// Accoda uno o più messaggi a tutti i partecipanti mantenendo un unico ordine
    /// globale per la stanza. Non esegue mai I/O mentre mantiene il lock.
    /// </summary>
    public bool Broadcast(Room room, IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(messages);

        lock (_gate)
        {
            if (!_rooms.TryGetValue(room.Id, out Room? current)
                || !ReferenceEquals(current, room))
            {
                return false;
            }

            foreach (string message in messages)
            {
                foreach (Participant participant in room.Participants)
                {
                    participant.TryEnqueue(message);
                }
            }

            return true;
        }
    }

    public IReadOnlyList<WaitingRequest> GetWaitingSnapshot(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        lock (_gate)
        {
            if (!_rooms.TryGetValue(room.Id, out Room? current)
                || !ReferenceEquals(current, room))
            {
                return Array.Empty<WaitingRequest>();
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            return room.Pending
                .Select(request => new WaitingRequest(
                    request.RequestId,
                    request.RequestedNickname,
                    request.PublicKey,
                    Math.Max(0, (int)(now - request.RequestedAt).TotalSeconds),
                    request.JoinMessage))
                .ToArray();
        }
    }

    public IReadOnlyList<RosterParticipant> GetRosterSnapshot(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        lock (_gate)
        {
            if (!_rooms.TryGetValue(room.Id, out Room? current)
                || !ReferenceEquals(current, room))
            {
                return Array.Empty<RosterParticipant>();
            }

            return room.Participants
                .Select(participant => new RosterParticipant(
                    participant.Nickname!,
                    participant.PublicKey!))
                .ToArray();
        }
    }

    public JoinResult Join(
        string roomId,
        string requestedNickname,
        string publicKey,
        Participant participant,
        string? joinMessage = null,
        Func<JoinResult, string>? initialMessageFactory = null)
    {
        ArgumentNullException.ThrowIfNull(participant);

        if (!IsValidRoomId(roomId))
        {
            return JoinResult.Error(ErrorCodes.InvalidRoomId);
        }

        if (!NicknameAllocator.TryNormalize(requestedNickname, out string? nickname))
        {
            return JoinResult.Error(ErrorCodes.InvalidNickname);
        }

        if (!ProtocolValueValidator.IsValidPublicKey(publicKey))
        {
            return JoinResult.Error(ErrorCodes.InvalidPublicKey);
        }

        if (!TryNormalizeJoinMessage(joinMessage, out string? normalizedJoinMessage))
        {
            return JoinResult.Error(ErrorCodes.InvalidJoinMessage);
        }

        string validNickname = nickname!;

        lock (_gate)
        {
            if (participant.CurrentRoom is not null)
            {
                return JoinResult.Error(ErrorCodes.AlreadyInRoom);
            }

            if (participant.IsPending)
            {
                return JoinResult.Error(ErrorCodes.AlreadyPending);
            }

            if (!_rooms.TryGetValue(roomId, out Room? room))
            {
                room = new Room(roomId, participant, _timeProvider.GetUtcNow());
                JoinResult joined = JoinResult.Joined(room, validNickname);
                if (initialMessageFactory is not null)
                {
                    participant.TryEnqueue(initialMessageFactory(joined));
                }

                participant.Enter(room, validNickname, publicKey);
                _rooms.Add(roomId, room);
                return joined;
            }

            if (room.ParticipantCount >= KryptaLimits.MaxParticipantsPerRoom)
            {
                return JoinResult.Error(ErrorCodes.RoomFull, room);
            }

            if (room.PendingCount >= KryptaLimits.MaxPendingPerRoom)
            {
                return JoinResult.Error(ErrorCodes.WaitingRoomFull, room);
            }

            JoinRequest request = new(
                Guid.NewGuid(),
                validNickname,
                publicKey,
                normalizedJoinMessage,
                participant,
                _timeProvider.GetUtcNow());
            room.AddPending(request);
            participant.Wait(room, request.RequestId, publicKey);
            return JoinResult.Pending(room, request.RequestId, request.RequestedNickname);
        }
    }

    public ApprovalResult Approve(
        Participant approver,
        Guid requestId,
        WrappedKey wrappedKey,
        int keyEpoch,
        Func<ApprovalResult, string>? initialMessageFactory = null)
    {
        ArgumentNullException.ThrowIfNull(approver);

        lock (_gate)
        {
            Room? room = approver.CurrentRoom;
            if (room is null)
            {
                return ApprovalResult.Error(ErrorCodes.NotInRoom);
            }

            JoinRequest? request = room.Pending.FirstOrDefault(
                item => item.RequestId == requestId);
            if (request is null)
            {
                return ApprovalResult.Error(ErrorCodes.UnknownRequest, room);
            }

            if (!ProtocolValueValidator.IsValidWrappedKey(wrappedKey))
            {
                return ApprovalResult.Error(ErrorCodes.InvalidApproval, room);
            }

            if (keyEpoch != room.KeyEpoch)
            {
                return ApprovalResult.Error(ErrorCodes.StaleKeyEpoch, room);
            }

            if (room.ParticipantCount >= KryptaLimits.MaxParticipantsPerRoom)
            {
                room.RemovePending(requestId, out _);
                request.Connection.Exit();
                return ApprovalResult.Rejected(
                    room,
                    request.Connection,
                    RejectionReasons.RoomFull,
                    request.RequestedNickname);
            }

            string effectiveNickname = NicknameAllocator.Allocate(
                request.RequestedNickname,
                room.Participants.Select(item => item.Nickname!));
            ApprovalResult approved = ApprovalResult.Approved(
                room,
                request.Connection,
                effectiveNickname,
                wrappedKey);
            if (initialMessageFactory is not null)
            {
                request.Connection.TryEnqueue(initialMessageFactory(approved));
            }

            room.RemovePending(requestId, out _);
            room.AddParticipant(request.Connection);
            request.Connection.Enter(room, effectiveNickname, request.PublicKey);

            return approved;
        }
    }

    public ApprovalResult Reject(Participant approver, Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(approver);

        lock (_gate)
        {
            Room? room = approver.CurrentRoom;
            if (room is null)
            {
                return ApprovalResult.Error(ErrorCodes.NotInRoom);
            }

            if (!room.RemovePending(requestId, out JoinRequest? request))
            {
                return ApprovalResult.Error(ErrorCodes.UnknownRequest, room);
            }

            request!.Connection.Exit();
            return ApprovalResult.Rejected(
                room,
                request.Connection,
                RejectionReasons.Rejected,
                request.RequestedNickname);
        }
    }

    /// <summary>
    /// Scade le richieste rimaste senza risposta oltre
    /// <see cref="KryptaLimits.JoinApprovalTimeoutSeconds"/> secondi (04 §4.1bis).
    /// Sta qui, e non in un servizio a parte, perché deve girare sotto lo stesso lock di
    /// <see cref="Approve"/> e <see cref="Reject"/>: una richiesta si evade una volta sola
    /// (I10), e una scadenza concorrente a un'approvazione non deve poter passare due volte.
    /// </summary>
    public IReadOnlyList<ExpiredRequest> ExpirePending()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan timeout = TimeSpan.FromSeconds(KryptaLimits.JoinApprovalTimeoutSeconds);
        List<ExpiredRequest> expired = [];

        lock (_gate)
        {
            foreach (Room room in _rooms.Values)
            {
                // room.Pending è una copia immutabile: rimuovere durante il ciclo è sicuro.
                foreach (JoinRequest request in room.Pending)
                {
                    if (now - request.RequestedAt < timeout)
                    {
                        continue;
                    }

                    if (!room.RemovePending(request.RequestId, out _))
                    {
                        continue;
                    }

                    request.Connection.Exit();
                    expired.Add(new ExpiredRequest(room, request.Connection));
                }
            }
        }

        return expired;
    }

    /// <summary>
    /// Rinomina la stanza. Il server rivalida sempre l'argomento, anche se il client
    /// lo ha già validato: un client modificato può inviare qualsiasi cosa (07 §9, regola 5).
    /// </summary>
    public OperationResult Rename(Participant participant, string? requestedName)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (_gate)
        {
            Room? room = participant.CurrentRoom;
            if (room is null)
            {
                return OperationResult.Error(ErrorCodes.NotInRoom);
            }

            if (!RoomNameNormalizer.TryNormalize(requestedName, out string normalized))
            {
                return OperationResult.Error(ErrorCodes.InvalidCommandArg);
            }

            room.Name = normalized;
            return OperationResult.Success();
        }
    }

    /// <summary>
    /// Elenco dei nickname in ordine di ingresso (invariante I7). Costruito al momento
    /// della richiesta e non conservato da nessuna parte.
    /// </summary>
    public IReadOnlyList<string> ListNicknames(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);

        lock (_gate)
        {
            return [.. room.Participants.Select(participant => participant.Nickname!)];
        }
    }

    public OperationResult Cancel(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (_gate)
        {
            Room? room = participant.PendingRoom;
            Guid? requestId = participant.PendingRequestId;
            if (room is null || requestId is null)
            {
                return OperationResult.Error(ErrorCodes.NotPending);
            }

            room.RemovePending(requestId.Value, out _);
            participant.Exit();
            return OperationResult.Success();
        }
    }

    public LeaveResult Leave(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (_gate)
        {
            Room? room = participant.CurrentRoom;
            if (room is null)
            {
                return LeaveResult.Error(ErrorCodes.NotInRoom);
            }

            room.RemoveParticipant(participant);
            participant.Exit();

            if (room.ParticipantCount > 0)
            {
                return LeaveResult.Left(false, Array.Empty<Participant>());
            }

            IReadOnlyList<JoinRequest> pending = room.ClearPending();
            Participant[] released = pending
                .Select(request => request.Connection)
                .ToArray();
            foreach (Participant requester in released)
            {
                requester.Exit();
            }

            _rooms.Remove(room.Id);
            return LeaveResult.Left(true, released);
        }
    }

    private static bool IsValidRoomId(string? roomId)
    {
        if (roomId is null || roomId.Length != KryptaLimits.RoomIdLength)
        {
            return false;
        }

        return roomId.All(character =>
            character is >= 'A' and <= 'Z' or >= '0' and <= '9');
    }

    private static bool TryNormalizeJoinMessage(string? value, out string? normalized)
    {
        normalized = null;
        if (value is null)
        {
            return true;
        }

        if (value.Any(char.IsControl))
        {
            return false;
        }

        string candidate = value.Trim();
        if (candidate.Length == 0)
        {
            return true;
        }

        if (candidate.Length > KryptaLimits.JoinMessageMaxLength)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

}
