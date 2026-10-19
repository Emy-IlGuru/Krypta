using System.Text;
using System.Threading.Channels;
using Krypta.Server.Connections;
using Krypta.Shared;

namespace Krypta.Server.Rooms;

/// <summary>
/// Rappresenta una singola connessione. Lo stato di appartenenza cambia sempre
/// tramite <see cref="RoomManager"/>.
/// </summary>
public sealed class Participant : IDisposable
{
    private readonly IClientConnection _connection;
    private readonly Channel<string> _sendQueue;
    private readonly CancellationTokenSource _disconnectSource = new();
    private MembershipState _membership = MembershipState.Outside;
    private long _lastSeenUnixMilliseconds;
    private int _disconnectReason;
    private int _outgoingCompleted;

    public Participant(
        IClientConnection connection,
        Guid? connectionId = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connection = connection;
        ConnectionId = connectionId ?? Guid.NewGuid();
        _lastSeenUnixMilliseconds = (timeProvider ?? TimeProvider.System)
            .GetUtcNow()
            .ToUnixTimeMilliseconds();
        _sendQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(
            KryptaLimits.SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Guid ConnectionId { get; }

    public string? PublicKey => Volatile.Read(ref _membership).PublicKey;

    public string? Nickname => Volatile.Read(ref _membership).Nickname;

    public Room? CurrentRoom => Volatile.Read(ref _membership).CurrentRoom;

    public bool IsPending => Volatile.Read(ref _membership).PendingRoom is not null;

    public Guid? PendingRequestId => Volatile.Read(ref _membership).PendingRequestId;

    public DateTimeOffset LastSeen =>
        DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _lastSeenUnixMilliseconds));

    public bool DisconnectRequested => DisconnectReason is not DisconnectReason.None;

    public DisconnectReason DisconnectReason =>
        (DisconnectReason)Volatile.Read(ref _disconnectReason);

    public CancellationToken DisconnectToken => _disconnectSource.Token;

    public void Touch(DateTimeOffset timestamp) =>
        Interlocked.Exchange(ref _lastSeenUnixMilliseconds, timestamp.ToUnixTimeMilliseconds());

    public bool TryEnqueue(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (DisconnectRequested || Volatile.Read(ref _outgoingCompleted) != 0)
        {
            return false;
        }

        if (_sendQueue.Writer.TryWrite(json))
        {
            return true;
        }

        RequestDisconnect(DisconnectReason.SlowClient);
        return false;
    }

    public async Task RunWriterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string json in _sendQueue.Reader.ReadAllAsync(cancellationToken))
            {
                byte[] payload = Encoding.UTF8.GetBytes(json);
                await _connection.SendTextAsync(payload, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            RequestDisconnect(DisconnectReason.WriterFailure);
            throw;
        }
    }

    public void CompleteOutgoing()
    {
        Interlocked.Exchange(ref _outgoingCompleted, 1);
        _sendQueue.Writer.TryComplete();
    }

    public void RequestDisconnect(DisconnectReason reason)
    {
        if (reason is DisconnectReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        if (Interlocked.CompareExchange(
                ref _disconnectReason,
                (int)reason,
                (int)DisconnectReason.None) == (int)DisconnectReason.None)
        {
            try
            {
                _disconnectSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Una snapshot dell'heartbeat può incrociare il cleanup finale.
            }
        }
    }

    internal ValueTask<ConnectionReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken) =>
        _connection.ReceiveAsync(buffer, cancellationToken);

    internal ValueTask CloseOutputAsync(
        System.Net.WebSockets.WebSocketCloseStatus closeStatus,
        string? description,
        CancellationToken cancellationToken) =>
        _connection.CloseOutputAsync(closeStatus, description, cancellationToken);

    internal void AbortConnection() => _connection.Abort();

    internal Room? PendingRoom => Volatile.Read(ref _membership).PendingRoom;

    internal void Enter(Room room, string nickname, string publicKey) =>
        Volatile.Write(ref _membership, MembershipState.InRoom(room, nickname, publicKey));

    internal void Wait(Room room, Guid requestId, string publicKey) =>
        Volatile.Write(ref _membership, MembershipState.Pending(room, requestId, publicKey));

    internal void Exit() => Volatile.Write(ref _membership, MembershipState.Outside);

    public void Dispose()
    {
        _disconnectSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record MembershipState(
        Room? CurrentRoom,
        Room? PendingRoom,
        Guid? PendingRequestId,
        string? Nickname,
        string? PublicKey)
    {
        public static MembershipState Outside { get; } = new(null, null, null, null, null);

        public static MembershipState InRoom(Room room, string nickname, string publicKey) =>
            new(room, null, null, nickname, publicKey);

        public static MembershipState Pending(Room room, Guid requestId, string publicKey) =>
            new(null, room, requestId, null, publicKey);
    }
}
