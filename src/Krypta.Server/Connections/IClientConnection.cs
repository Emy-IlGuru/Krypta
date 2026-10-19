using System.Net.WebSockets;

namespace Krypta.Server.Connections;

/// <summary>
/// Astrazione minima del WebSocket, usata per isolare il ciclo di connessione nei test.
/// </summary>
public interface IClientConnection
{
    /// <summary>
    /// Indirizzo remoto, usato <b>solo</b> dal limite sui join (04 §4.6). Non va loggato
    /// insieme all'ID stanza né al nickname: insieme sarebbero un identificatore.
    /// </summary>
    string? RemoteAddress => null;

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
