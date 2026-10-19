using System.Text.Json;
using Krypta.Server.Rooms;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Tests.TestDoubles;

/// <summary>
/// Un partecipante finto con la propria coda di invio, per leggere i messaggi
/// che il server gli ha davvero mandato.
/// </summary>
internal sealed class TestParticipant
{
    private readonly FakeClientConnection _connection;
    private readonly Task _writer;

    public TestParticipant(string? remoteAddress = null)
    {
        _connection = new FakeClientConnection { RemoteAddress = remoteAddress };
        Participant = new Participant(_connection);
        _writer = Participant.RunWriterAsync(CancellationToken.None);
    }

    public Participant Participant { get; }

    public async Task<ServerMessage[]> FinishAsync()
    {
        Participant.CompleteOutgoing();
        await _writer;
        Participant.Dispose();

        return _connection.Sent
            .Select(json => JsonSerializer.Deserialize(
                json,
                ProtocolJsonContext.Default.ServerMessage)!)
            .ToArray();
    }
}

internal sealed class StoppedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal static class ProtocolFixture
{
    public static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);

    public static string SerializeClient(ClientMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ClientMessage);

    public static string PublicKey()
    {
        byte[] publicKey = new byte[KryptaLimits.PublicKeyBytes];
        publicKey[0] = 0x04;
        return Convert.ToBase64String(publicKey);
    }

    public static WrappedKey WrappedKey()
    {
        byte[] epk = new byte[KryptaLimits.PublicKeyBytes];
        epk[0] = 0x04;
        return new WrappedKey(
            Convert.ToBase64String(epk),
            Convert.ToBase64String(new byte[KryptaLimits.NonceBytes]),
            Convert.ToBase64String(new byte[KryptaLimits.WrappedKeyCiphertextBytes]));
    }

    /// <summary>Le sole righe di sistema, nell'ordine in cui sono arrivate.</summary>
    public static SystemServerMessage[] Systems(this ServerMessage[] messages) =>
        messages.OfType<SystemServerMessage>().ToArray();

    public static string[] Kinds(this ServerMessage[] messages) =>
        messages.Select(message => message switch
        {
            SystemServerMessage system => $"system:{system.Kind}",
            ClearServerMessage => "clear",
            ParticipantsServerMessage => "participants",
            WaitingServerMessage => "waiting",
            RosterServerMessage => "roster",
            JoinedServerMessage => "joined",
            ErrorServerMessage error => $"error:{error.Code}",
            _ => message.GetType().Name
        }).ToArray();
}

/// <summary>Tempo controllabile, per far scorrere le finestre di frequenza.</summary>
internal sealed class MovableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}
