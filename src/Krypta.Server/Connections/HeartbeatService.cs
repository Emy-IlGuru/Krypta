using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;
using Krypta.Shared;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Connections;

public sealed class HeartbeatService(
    ConnectionRegistry registry,
    ProtocolMessageSender sender,
    JoinRateLimiter joinRateLimiter,
    TimeProvider timeProvider,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    public void RunOnce()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        TimeSpan timeout = TimeSpan.FromSeconds(KryptaLimits.ConnectionTimeoutSeconds);

        // Le voci scadute del limite sui join vanno ripulite, altrimenti il dizionario
        // cresce quanto il numero di indirizzi che hanno mai bussato (04 §4.6).
        int pruned = joinRateLimiter.Prune();
        if (pruned > 0)
        {
            logger.LogDebug("Voci scadute rimosse dal limite sui join: {Pruned}", pruned);
        }

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
