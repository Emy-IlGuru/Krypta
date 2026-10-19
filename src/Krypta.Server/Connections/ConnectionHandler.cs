using System.Net.WebSockets;
using System.Text;
using Krypta.Server.Messaging;
using Krypta.Server.Rooms;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Connections;

public sealed class ConnectionHandler(
    ConnectionRegistry registry,
    MessageDispatcher dispatcher,
    ProtocolMessageSender sender,
    ILogger<ConnectionHandler> logger,
    TimeProvider timeProvider)
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task HandleAsync(
        IClientConnection connection,
        CancellationToken cancellationToken)
    {
        using Participant participant = new(connection, timeProvider: timeProvider);
        if (!registry.Register(participant))
        {
            throw new InvalidOperationException("ConnectionId duplicato.");
        }

        logger.LogDebug(
            "Connessione aperta. ConnectionId={ConnectionId}",
            participant.ConnectionId);

        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                participant.DisconnectToken);
        Task writer = participant.RunWriterAsync(linkedSource.Token);
        RemoteClose? remoteClose = null;

        try
        {
            remoteClose = await RunReaderAsync(participant, linkedSource.Token);
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Connessione interrotta. ConnectionId={ConnectionId}",
                participant.ConnectionId);
        }
        finally
        {
            dispatcher.Disconnect(participant);
            registry.Unregister(participant);
            participant.CompleteOutgoing();
            linkedSource.Cancel();

            try
            {
                await writer;
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "Writer terminato. ConnectionId={ConnectionId}",
                    participant.ConnectionId);
            }

            await CloseConnectionAsync(participant, remoteClose);
            logger.LogDebug(
                "Connessione chiusa. ConnectionId={ConnectionId}",
                participant.ConnectionId);
        }
    }

    private async Task<RemoteClose?> RunReaderAsync(
        Participant participant,
        CancellationToken cancellationToken)
    {
        byte[] messageBuffer = new byte[KryptaLimits.MaxFrameBytes];
        byte[] discardBuffer = new byte[4_096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int messageLength = 0;
            WebSocketMessageType? messageType = null;
            bool malformed = false;

            while (true)
            {
                Memory<byte> target = malformed || messageLength == messageBuffer.Length
                    ? discardBuffer
                    : messageBuffer.AsMemory(messageLength);
                ConnectionReceiveResult result = await participant.ReceiveAsync(
                    target,
                    cancellationToken);

                if (result.MessageType is WebSocketMessageType.Close)
                {
                    return new RemoteClose(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription);
                }

                participant.Touch(timeProvider.GetUtcNow());
                messageType ??= result.MessageType;
                if (result.MessageType != messageType
                    || result.MessageType is not WebSocketMessageType.Text)
                {
                    malformed = true;
                }

                if (!malformed)
                {
                    messageLength += result.Count;
                    if (!result.EndOfMessage && messageLength == messageBuffer.Length)
                    {
                        malformed = true;
                    }
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            if (malformed)
            {
                sender.SendError(participant, ErrorCodes.Malformed);
                continue;
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(messageBuffer, 0, messageLength);
            }
            catch (DecoderFallbackException)
            {
                sender.SendError(participant, ErrorCodes.Malformed);
                continue;
            }

            await dispatcher.DispatchAsync(participant, json, cancellationToken);
        }

        return null;
    }

    private static async Task CloseConnectionAsync(
        Participant participant,
        RemoteClose? remoteClose)
    {
        (WebSocketCloseStatus status, string description) = GetCloseDetails(
            participant.DisconnectReason,
            remoteClose);
        using CancellationTokenSource closeTimeout = new(TimeSpan.FromSeconds(2));

        try
        {
            await participant.CloseOutputAsync(
                status,
                description,
                closeTimeout.Token);
        }
        catch
        {
            participant.AbortConnection();
        }
    }

    private static (WebSocketCloseStatus Status, string Description) GetCloseDetails(
        DisconnectReason reason,
        RemoteClose? remoteClose) => reason switch
        {
            DisconnectReason.SlowClient =>
                (WebSocketCloseStatus.PolicyViolation, "Client troppo lento."),
            DisconnectReason.HeartbeatTimeout =>
                (WebSocketCloseStatus.PolicyViolation, "Heartbeat scaduto."),
            DisconnectReason.ServerShutdown =>
                (WebSocketCloseStatus.EndpointUnavailable, "Server in arresto."),
            DisconnectReason.WriterFailure =>
                (WebSocketCloseStatus.InternalServerError, "Errore di invio."),
            _ when remoteClose is not null =>
                (remoteClose.Status, remoteClose.Description ?? string.Empty),
            _ =>
                (WebSocketCloseStatus.NormalClosure, string.Empty)
        };

    private sealed record RemoteClose(
        WebSocketCloseStatus Status,
        string? Description);
}
