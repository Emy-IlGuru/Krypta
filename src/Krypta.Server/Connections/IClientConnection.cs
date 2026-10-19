using System.Net.WebSockets;

namespace Krypta.Server.Connections;

/// <summary>
/// Astrazione minima del WebSocket, usata per isolare il ciclo di connessione nei test.
/// </summary>
public interface IClientConnection
{
    ValueTask<ConnectionReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken);

    ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Payload,
        CancellationToken cancellationToken);

    ValueTask CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? description,
        CancellationToken cancellationToken);

    void Abort();
}
