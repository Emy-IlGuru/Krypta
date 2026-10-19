using System.Net.WebSockets;

namespace Krypta.Server.Connections;

public sealed class WebSocketClientConnection(WebSocket socket) : IClientConnection
{
    public async ValueTask<ConnectionReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ValueWebSocketReceiveResult result = await socket.ReceiveAsync(
            buffer,
            cancellationToken);
        return new ConnectionReceiveResult(
            result.Count,
            result.EndOfMessage,
            result.MessageType,
            socket.CloseStatus,
            socket.CloseStatusDescription);
    }

    public ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Payload,
        CancellationToken cancellationToken) =>
        socket.SendAsync(
            utf8Payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    public ValueTask CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? description,
        CancellationToken cancellationToken) =>
        new(socket.CloseOutputAsync(closeStatus, description, cancellationToken));

    public void Abort() => socket.Abort();
}
