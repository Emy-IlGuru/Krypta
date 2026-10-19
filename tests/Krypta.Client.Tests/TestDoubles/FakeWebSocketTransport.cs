using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Krypta.Client.Services;
using ClientReceiveResult = Krypta.Client.Services.WebSocketReceiveResult;

namespace Krypta.Client.Tests.TestDoubles;

internal sealed class FakeWebSocketTransport : IWebSocketTransport
{
    private readonly Channel<ClientReceiveResult> _Incoming = Channel.CreateUnbounded<ClientReceiveResult>();
    private readonly ConcurrentQueue<byte[]> _Chunks = new();
    private string? _RequestedSubProtocol;

    public WebSocketState State { get; private set; } = WebSocketState.None;

    public string? SubProtocol { get; private set; }

    public Uri? ConnectedUri { get; private set; }

    public ConcurrentQueue<string> SentMessages { get; } = new();

    public void AddSubProtocol(string subProtocol) => _RequestedSubProtocol = subProtocol;

    public ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedUri = uri;
        SubProtocol = _RequestedSubProtocol;
        State = WebSocketState.Open;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ClientReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ClientReceiveResult result = await _Incoming.Reader.ReadAsync(cancellationToken);
        if (result.Count > 0 && _Chunks.TryDequeue(out byte[]? bytes))
        {
            bytes.CopyTo(buffer);
        }

        return result;
    }

    public ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Payload,
        CancellationToken cancellationToken)
    {
        SentMessages.Enqueue(Encoding.UTF8.GetString(utf8Payload.Span));
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? description,
        CancellationToken cancellationToken)
    {
        State = WebSocketState.CloseSent;
        return ValueTask.CompletedTask;
    }

    public void Abort()
    {
        State = WebSocketState.Aborted;
        _Incoming.Writer.TryComplete(new WebSocketException("Socket interrotto."));
    }

    public ValueTask DisposeAsync()
    {
        if (State != WebSocketState.Aborted)
        {
            State = WebSocketState.Closed;
            _Incoming.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    public void QueueText(string json, int? firstFragmentBytes = null)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        int firstCount = firstFragmentBytes.GetValueOrDefault(payload.Length);
        if (firstCount <= 0 || firstCount >= payload.Length)
        {
            QueueChunk(payload, endOfMessage: true, WebSocketMessageType.Text);
            return;
        }

        QueueChunk(payload[..firstCount], endOfMessage: false, WebSocketMessageType.Text);
        QueueChunk(payload[firstCount..], endOfMessage: true, WebSocketMessageType.Text);
    }

    public void QueueClose(string description = "Chiusura di prova")
    {
        State = WebSocketState.CloseReceived;
        QueueChunk([], endOfMessage: true, WebSocketMessageType.Close, description);
    }

    private void QueueChunk(
        byte[] payload,
        bool endOfMessage,
        WebSocketMessageType messageType,
        string? closeDescription = null)
    {
        _Chunks.Enqueue(payload);
        _Incoming.Writer.TryWrite(new ClientReceiveResult(
            payload.Length,
            endOfMessage,
            messageType,
            messageType == WebSocketMessageType.Close
                ? WebSocketCloseStatus.NormalClosure
                : null,
            closeDescription));
    }
}
