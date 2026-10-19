namespace Krypta.Shared.Protocol;

public sealed record WrappedKey(
    string Epk,
    string Nonce,
    string Ciphertext);

public sealed record MessageEnvelope(
    int Seq,
    int KeyEpoch,
    string Nonce,
    string Ciphertext);

public sealed record RekeyRecipient(
    string Nickname,
    WrappedKey WrappedKey);

public sealed record RosterParticipant(
    string Nickname,
    string PublicKey);

public sealed record WaitingRequest(
    Guid RequestId,
    string Nickname,
    string PublicKey,
    int WaitingSeconds,
    string? JoinMessage = null);
