using System.Net.WebSockets;

namespace Krypta.Server.Connections;

public readonly record struct ConnectionReceiveResult(
    int Count,
    bool EndOfMessage,
    WebSocketMessageType MessageType,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseStatusDescription = null);
