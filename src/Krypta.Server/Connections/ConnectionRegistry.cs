using System.Collections.Concurrent;
using Krypta.Server.Rooms;

namespace Krypta.Server.Connections;

public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, Participant> _participants = new();

    public int Count => _participants.Count;

    public bool Register(Participant participant) =>
        _participants.TryAdd(participant.ConnectionId, participant);

    public bool Unregister(Participant participant) =>
        _participants.TryRemove(participant.ConnectionId, out _);

    public IReadOnlyList<Participant> Snapshot() => _participants.Values.ToArray();
}
