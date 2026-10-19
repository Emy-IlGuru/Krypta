using System.Collections.ObjectModel;

namespace Krypta.Server.Rooms;

/// <summary>
/// Stato effimero di una stanza. Non contiene messaggi o storico.
/// </summary>
public sealed class Room
{
    private IReadOnlyList<Participant> _participants;
    private IReadOnlyList<JoinRequest> _pending = Array.Empty<JoinRequest>();

    internal Room(
        string id,
        Participant firstParticipant,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = $"RM-{id}";
        CreatedAt = createdAt;
        KeyEpoch = 1;
        _participants = ReadOnly([firstParticipant]);
    }

    public string Id { get; }

    /// <summary>
    /// Etichetta di visualizzazione, mutabile con <c>/NameRoom</c>. Non rende la stanza
    /// cercabile: l'ID resta l'unico modo per collegarsi. Muore con la stanza.
    /// </summary>
    public string Name { get; internal set; }

    public DateTimeOffset CreatedAt { get; }

    public int KeyEpoch { get; internal set; }

    public IReadOnlyList<Participant> Participants => Volatile.Read(ref _participants);

    public IReadOnlyList<JoinRequest> Pending => Volatile.Read(ref _pending);

    public int ParticipantCount => Volatile.Read(ref _participants).Count;

    public int PendingCount => Volatile.Read(ref _pending).Count;

    internal void AddParticipant(Participant participant) =>
        Volatile.Write(ref _participants, ReadOnly([.. _participants, participant]));

    internal void RemoveParticipant(Participant participant) =>
        Volatile.Write(
            ref _participants,
            ReadOnly(_participants.Where(item => !ReferenceEquals(item, participant))));

    internal void AddPending(JoinRequest request) =>
        Volatile.Write(ref _pending, ReadOnly([.. _pending, request]));

    internal bool RemovePending(Guid requestId, out JoinRequest? request)
    {
        request = _pending.FirstOrDefault(item => item.RequestId == requestId);
        if (request is null)
        {
            return false;
        }

        Volatile.Write(
            ref _pending,
            ReadOnly(_pending.Where(item => item.RequestId != requestId)));
        return true;
    }

    internal IReadOnlyList<JoinRequest> ClearPending()
    {
        IReadOnlyList<JoinRequest> released = _pending;
        Volatile.Write(ref _pending, Array.Empty<JoinRequest>());
        return released;
    }

    private static ReadOnlyCollection<T> ReadOnly<T>(IEnumerable<T> source) =>
        Array.AsReadOnly(source.ToArray());
}
