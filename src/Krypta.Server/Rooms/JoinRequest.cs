namespace Krypta.Server.Rooms;

/// <summary>
/// Richiesta effimera di ingresso. Il nickname viene disambiguato soltanto
/// quando la richiesta viene approvata.
/// </summary>
public sealed record JoinRequest(
    Guid RequestId,
    string RequestedNickname,
    string PublicKey,
    string? JoinMessage,
    Participant Connection,
    DateTimeOffset RequestedAt);
