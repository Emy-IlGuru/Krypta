using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Krypta.Server.Connections;

namespace Krypta.Server.Tests.TestDoubles;

internal sealed class FakeClientConnection : IClientConnection
{
    private readonly Channel<IncomingChunk> _incoming = Channel.CreateUnbounded<IncomingChunk>();
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly SemaphoreSlim _sentSignal = new(0);
    private IncomingChunk? _current;
    private int _currentOffset;
    private int _activeSends;
    private int _maximumConcurrentSends;

    public IReadOnlyList<string> Sent => _sent.ToArray();

    public int MaximumConcurrentSends => Volatile.Read(ref _maximumConcurrentSends);

    public TimeSpan SendDelay { get; init; }

    public bool Aborted { get; private set; }

    public WebSocketCloseStatus? OutputCloseStatus { get; private set; }

    public void QueueText(string json, bool endOfMessage = true) =>
        _incoming.Writer.TryWrite(new IncomingChunk(
            Encoding.UTF8.GetBytes(json),
            WebSocketMessageType.Text,
            endOfMessage));

    public void QueueBinary(byte[] bytes, bool endOfMessage = true) =>
        _incoming.Writer.TryWrite(new IncomingChunk(
            bytes,
            WebSocketMessageType.Binary,
            endOfMessage));

    public void QueueClose(
        WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
        string? description = null) =>
        _incoming.Writer.TryWrite(new IncomingChunk(
            Array.Empty<byte>(),
            WebSocketMessageType.Close,
            true,
            status,
            description));

    public void FailIncoming(Exception exception) => _incoming.Writer.TryComplete(exception);

    public async Task WaitForSentCountAsync(
        int expectedCount,
        TimeSpan? timeout = null)
    {
        using CancellationTokenSource timeoutSource = new(
            timeout ?? TimeSpan.FromSeconds(5));

        while (_sent.Count < expectedCount)
        {
            await _sentSignal.WaitAsync(timeoutSource.Token);
        }
    }

    public async ValueTask<ConnectionReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        _current ??= await _incoming.Reader.ReadAsync(cancellationToken);
        int remaining = _current.Payload.Length - _currentOffset;
        int copied = Math.Min(buffer.Length, remaining);
        _current.Payload.AsMemory(_currentOffset, copied).CopyTo(buffer);
        _currentOffset += copied;

        bool chunkFinished = _currentOffset == _current.Payload.Length;
        ConnectionReceiveResult result = new(
            copied,
            chunkFinished && _current.EndOfMessage,
            _current.MessageType,
            _current.CloseStatus,
            _current.CloseStatusDescription);

        if (chunkFinished)
        {
            _current = null;
            _currentOffset = 0;
        }

        return result;
    }

    public async ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Payload,
        CancellationToken cancellationToken)
    {
        int active = Interlocked.Increment(ref _activeSends);
        SetMaximum(active);

        try
        {
            if (SendDelay > TimeSpan.Zero)
            {
                await Task.Delay(SendDelay, cancellationToken);
            }

            _sent.Enqueue(Encoding.UTF8.GetString(utf8Payload.Span));
            _sentSignal.Release();
        }
        finally
        {
            Interlocked.Decrement(ref _activeSends);
        }
    }

    public ValueTask CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? description,
        CancellationToken cancellationToken)
    {
        OutputCloseStatus = closeStatus;
        return ValueTask.CompletedTask;
    }

    public void Abort() => Aborted = true;

    private void SetMaximum(int active)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref _maximumConcurrentSends);
            if (observed >= active)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(
            ref _maximumConcurrentSends,
            active,
            observed) != observed);
    }

    private sealed record IncomingChunk(
        byte[] Payload,
        WebSocketMessageType MessageType,
        bool EndOfMessage,
        WebSocketCloseStatus? CloseStatus = null,
        string? CloseStatusDescription = null);
}
