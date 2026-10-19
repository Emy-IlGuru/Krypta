using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Client.Services;

public sealed record KryptaDisconnectedEventArgs(
    bool Expected,
    string Message);

public sealed class KryptaConnection : IAsyncDisposable
{
    public const string SubProtocol = "krypta.v1";
    public const int ConnectTimeoutSeconds = 5;
    public const int ServerPingTimeoutSeconds = 90;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly Func<IWebSocketTransport> _TransportFactory;
    private readonly IUiDispatcher _UiDispatcher;
    private readonly TimeProvider _TimeProvider;
    private readonly SemaphoreSlim _LifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _SendGate = new(1, 1);

    private IWebSocketTransport? _Transport;
    private CancellationTokenSource? _ConnectionCancellation;
    private Task? _ReceiveTask;
    private Task? _HeartbeatTask;
    private long _LastPingTimestamp;
    private int _DisconnectNotified;

    public KryptaConnection()
        : this(
            static () => new ClientWebSocketTransport(),
            new AvaloniaUiDispatcher(),
            TimeProvider.System)
    {
    }

    internal KryptaConnection(
        Func<IWebSocketTransport> transportFactory,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider)
    {
        _TransportFactory = transportFactory;
        _UiDispatcher = uiDispatcher;
        _TimeProvider = timeProvider;
    }

    public bool IsConnected => _Transport?.State == WebSocketState.Open;

    public event EventHandler<JoinedServerMessage>? Joined;

    public event EventHandler<ChatServerMessage>? MessageReceived;

    public event EventHandler<SystemServerMessage>? SystemReceived;

    public event EventHandler? ClearReceived;

    public event EventHandler<ParticipantsServerMessage>? ParticipantsChanged;

    public event EventHandler<SystemServerMessage>? RoomRenamed;

    public event EventHandler<PendingServerMessage>? PendingStarted;

    public event EventHandler<WaitingServerMessage>? WaitingChanged;

    public event EventHandler<RejectedServerMessage>? Rejected;

    public event EventHandler<LeftServerMessage>? Left;

    public event EventHandler<RosterServerMessage>? RosterChanged;

    public event EventHandler<RekeyedServerMessage>? Rekeyed;

    public event EventHandler<ErrorServerMessage>? ErrorReceived;

    public event EventHandler<KryptaDisconnectedEventArgs>? Disconnected;

    public async Task ConnectAsync(
        string host,
        int port,
        bool useTls,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("L'host del server è obbligatorio.", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (!useTls && !string.Equals(host, "127.0.0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La connessione senza TLS è ammessa solo verso 127.0.0.1.");
        }

        await _LifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_Transport is not null)
            {
                throw new InvalidOperationException("Una connessione è già attiva.");
            }

            IWebSocketTransport transport = _TransportFactory();
            transport.AddSubProtocol(SubProtocol);
            Uri endpoint = new UriBuilder(useTls ? "wss" : "ws", host, port, "/ws").Uri;

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

            try
            {
                await transport.ConnectAsync(endpoint, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await transport.DisposeAsync();
                throw new TimeoutException("Il server non ha risposto entro 5 secondi.");
            }
            catch
            {
                await transport.DisposeAsync();
                throw;
            }

            if (transport.State != WebSocketState.Open
                || !string.Equals(transport.SubProtocol, SubProtocol, StringComparison.Ordinal))
            {
                transport.Abort();
                await transport.DisposeAsync();
                throw new WebSocketException("Il server ha rifiutato il sotto-protocollo krypta.v1.");
            }

            CancellationTokenSource connectionCancellation = new();
            _Transport = transport;
            _ConnectionCancellation = connectionCancellation;
            _LastPingTimestamp = _TimeProvider.GetTimestamp();
            Interlocked.Exchange(ref _DisconnectNotified, 0);
            _ReceiveTask = ReceiveLoopAsync(transport, connectionCancellation.Token);
            _HeartbeatTask = MonitorServerHeartbeatAsync(transport, connectionCancellation.Token);
        }
        finally
        {
            _LifecycleGate.Release();
        }
    }

    public Task JoinAsync(
        string roomId,
        string nickname,
        string publicKey,
        string? joinMessage = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            new JoinClientMessage(roomId, nickname, publicKey, joinMessage),
            cancellationToken);

    public Task ApproveAsync(
        Guid requestId,
        WrappedKey wrappedKey,
        int keyEpoch,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            new ApproveClientMessage(requestId, wrappedKey, keyEpoch),
            cancellationToken);

    public Task RejectAsync(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        SendAsync(new RejectClientMessage(requestId), cancellationToken);

    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new CancelClientMessage(), cancellationToken);

    public Task SendMessageAsync(
        int seq,
        int keyEpoch,
        string nonce,
        string ciphertext,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            new ChatClientMessage(seq, keyEpoch, nonce, ciphertext),
            cancellationToken);

    public Task SendCommandAsync(
        string name,
        string? argument = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(new CommandClientMessage(name, argument), cancellationToken);

    public Task RekeyAsync(
        int keyEpoch,
        IReadOnlyList<RekeyRecipient> keys,
        CancellationToken cancellationToken = default) =>
        SendAsync(new RekeyClientMessage(keyEpoch, keys), cancellationToken);

    public Task LeaveAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new LeaveClientMessage(), cancellationToken);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IWebSocketTransport? transport;
        CancellationTokenSource? connectionCancellation;
        Task? receiveTask;
        Task? heartbeatTask;

        await _LifecycleGate.WaitAsync(cancellationToken);
        try
        {
            transport = _Transport;
            connectionCancellation = _ConnectionCancellation;
            receiveTask = _ReceiveTask;
            heartbeatTask = _HeartbeatTask;
            ClearConnectionState();
        }
        finally
        {
            _LifecycleGate.Release();
        }

        if (transport is null)
        {
            return;
        }

        try
        {
            if (transport.State == WebSocketState.Open)
            {
                await transport.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Chiusura del client",
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is WebSocketException
            or IOException
            or OperationCanceledException)
        {
            transport.Abort();
        }
        finally
        {
            connectionCancellation?.Cancel();
        }

        await AwaitBackgroundTaskAsync(receiveTask);
        await AwaitBackgroundTaskAsync(heartbeatTask);
        connectionCancellation?.Dispose();
        await transport.DisposeAsync();
        NotifyDisconnected(expected: true, "Connessione chiusa.");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _LifecycleGate.Dispose();
        _SendGate.Dispose();
    }

    private async Task SendAsync(
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        IWebSocketTransport transport = _Transport
            ?? throw new InvalidOperationException("Il client non è connesso.");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            ProtocolJsonContext.Default.ClientMessage);
        if (payload.Length > KryptaLimits.MaxFrameBytes)
        {
            throw new InvalidOperationException("Il messaggio supera il limite del protocollo.");
        }

        await _SendGate.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(transport, _Transport)
                || transport.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("Il client non è connesso.");
            }

            await transport.SendTextAsync(payload, cancellationToken);
        }
        finally
        {
            _SendGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(
        IWebSocketTransport transport,
        CancellationToken cancellationToken)
    {
        string disconnectMessage = "Connessione al server persa.";
        try
        {
            byte[] buffer = new byte[4096];
            using MemoryStream messageBuffer = new(KryptaLimits.MaxFrameBytes);

            while (!cancellationToken.IsCancellationRequested)
            {
                messageBuffer.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await transport.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        disconnectMessage = result.CloseStatusDescription
                            ?? "Il server ha chiuso la connessione.";
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Text
                        || messageBuffer.Length + result.Count > KryptaLimits.MaxFrameBytes)
                    {
                        disconnectMessage = "Il server ha inviato un messaggio non valido.";
                        return;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = StrictUtf8.GetString(
                    messageBuffer.GetBuffer(),
                    0,
                    checked((int)messageBuffer.Length));
                ServerMessage? message = JsonSerializer.Deserialize(
                    json,
                    ProtocolJsonContext.Default.ServerMessage);
                if (message is null)
                {
                    disconnectMessage = "Il server ha inviato un messaggio non valido.";
                    return;
                }

                if (message is PingServerMessage)
                {
                    _LastPingTimestamp = _TimeProvider.GetTimestamp();
                    await SendAsync(new PongClientMessage(), cancellationToken);
                    continue;
                }

                Dispatch(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is WebSocketException
            or IOException
            or JsonException
            or DecoderFallbackException
            or InvalidOperationException)
        {
            disconnectMessage = "Connessione al server persa.";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await CompleteUnexpectedDisconnectAsync(transport, disconnectMessage);
            }
        }
    }

    private async Task MonitorServerHeartbeatAsync(
        IWebSocketTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _TimeProvider, cancellationToken);
                if (_TimeProvider.GetElapsedTime(_LastPingTimestamp)
                    < TimeSpan.FromSeconds(ServerPingTimeoutSeconds))
                {
                    continue;
                }

                transport.Abort();
                await CompleteUnexpectedDisconnectAsync(
                    transport,
                    "Il server non invia heartbeat da 90 secondi.");
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CompleteUnexpectedDisconnectAsync(
        IWebSocketTransport transport,
        string message)
    {
        CancellationTokenSource? connectionCancellation = null;

        await _LifecycleGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_Transport, transport))
            {
                return;
            }

            connectionCancellation = _ConnectionCancellation;
            ClearConnectionState();
        }
        finally
        {
            _LifecycleGate.Release();
        }

        connectionCancellation?.Cancel();
        connectionCancellation?.Dispose();
        transport.Abort();
        await transport.DisposeAsync();
        NotifyDisconnected(expected: false, message);
    }

    private void Dispatch(ServerMessage message)
    {
        switch (message)
        {
            case JoinedServerMessage joined:
                Post(() => Joined?.Invoke(this, joined));
                break;
            case ChatServerMessage chat:
                Post(() => MessageReceived?.Invoke(this, chat));
                break;
            case SystemServerMessage system:
                Post(() =>
                {
                    SystemReceived?.Invoke(this, system);
                    if (system.Kind == SystemKinds.RoomRenamed)
                    {
                        RoomRenamed?.Invoke(this, system);
                    }
                });
                break;
            case ClearServerMessage:
                Post(() => ClearReceived?.Invoke(this, EventArgs.Empty));
                break;
            case ParticipantsServerMessage participants:
                Post(() => ParticipantsChanged?.Invoke(this, participants));
                break;
            case PendingServerMessage pending:
                Post(() => PendingStarted?.Invoke(this, pending));
                break;
            case WaitingServerMessage waiting:
                Post(() => WaitingChanged?.Invoke(this, waiting));
                break;
            case RejectedServerMessage rejected:
                Post(() => Rejected?.Invoke(this, rejected));
                break;
            case LeftServerMessage left:
                Post(() => Left?.Invoke(this, left));
                break;
            case RosterServerMessage roster:
                Post(() => RosterChanged?.Invoke(this, roster));
                break;
            case RekeyedServerMessage rekeyed:
                Post(() => Rekeyed?.Invoke(this, rekeyed));
                break;
            case ErrorServerMessage error:
                Post(() => ErrorReceived?.Invoke(this, error));
                break;
        }
    }

    private void NotifyDisconnected(bool expected, string message)
    {
        if (Interlocked.Exchange(ref _DisconnectNotified, 1) == 0)
        {
            Post(() => Disconnected?.Invoke(
                this,
                new KryptaDisconnectedEventArgs(expected, message)));
        }
    }

    private void Post(Action action) => _UiDispatcher.Post(action);

    private void ClearConnectionState()
    {
        _Transport = null;
        _ConnectionCancellation = null;
        _ReceiveTask = null;
        _HeartbeatTask = null;
    }

    private static async Task AwaitBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
