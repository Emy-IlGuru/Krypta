using System.Net.WebSockets;

namespace Krypta.Client.Services;

internal readonly record struct WebSocketReceiveResult(
    int Count,
    bool EndOfMessage,
    WebSocketMessageType MessageType,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseStatusDescription = null);

internal interface IWebSocketTransport : IAsyncDisposable
{
    WebSocketState State { get; }

    string? SubProtocol { get; }

    void AddSubProtocol(string subProtocol);

    ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken);

    ValueTask<WebSocketReceiveResult> ReceiveAsync(
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

internal sealed class ClientWebSocketTransport : IWebSocketTransport
{
    private readonly ClientWebSocket _Socket = new();

    public WebSocketState State => _Socket.State;

    public string? SubProtocol => _Socket.SubProtocol;

    public void AddSubProtocol(string subProtocol) =>
        _Socket.Options.AddSubProtocol(subProtocol);

    public async ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        await _Socket.ConnectAsync(uri, cancellationToken);

    public async ValueTask<WebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ValueWebSocketReceiveResult result = await _Socket.ReceiveAsync(buffer, cancellationToken);
        return new WebSocketReceiveResult(
            result.Count,
            result.EndOfMessage,
            result.MessageType,
            _Socket.CloseStatus,
            _Socket.CloseStatusDescription);
    }

    public ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Payload,
        CancellationToken cancellationToken) =>
        _Socket.SendAsync(
            utf8Payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    public ValueTask CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? description,
        CancellationToken cancellationToken) =>
        new(_Socket.CloseOutputAsync(closeStatus, description, cancellationToken));

    public void Abort() => _Socket.Abort();

    public ValueTask DisposeAsync()
    {
        _Socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
