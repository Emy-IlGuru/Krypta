using Krypta.Server.Messaging;
using Krypta.Server.Rooms;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Connections;

public sealed class HeartbeatService(
    ConnectionRegistry registry,
    ProtocolMessageSender sender,
    TimeProvider timeProvider,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    public void RunOnce()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        TimeSpan timeout = TimeSpan.FromSeconds(KryptaLimits.ConnectionTimeoutSeconds);

        foreach (Participant participant in registry.Snapshot())
        {
            if (now - participant.LastSeen >= timeout)
            {
                logger.LogDebug(
                    "Heartbeat scaduto. ConnectionId={ConnectionId}",
                    participant.ConnectionId);
                participant.RequestDisconnect(DisconnectReason.HeartbeatTimeout);
                continue;
            }

            sender.Send(participant, new PingServerMessage());
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (Participant participant in registry.Snapshot())
        {
            participant.RequestDisconnect(DisconnectReason.ServerShutdown);
        }

        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(KryptaLimits.PingIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
                RunOnce();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
