using Krypta.Server.Messaging;

namespace Krypta.Server.Rooms;

/// <summary>
/// Scade le richieste della sala d'attesa rimaste senza risposta (RF-S-A11, 04 §4.1bis).
/// Senza, il server prometterebbe 120 secondi nel `pending` — e il client ci fa sopra un
/// conto alla rovescia — senza mantenerli: una richiesta resterebbe appesa finché la
/// stanza non muore.
/// </summary>
public sealed class PendingExpiryService(
    MessageDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<PendingExpiryService> logger) : BackgroundService
{
    /// <summary>
    /// Il passo è corto di proposito. La scadenza è un tempo che l'utente **vede**
    /// scorrere: con il passo dell'heartbeat, 30 secondi, una richiesta potrebbe morire a
    /// 150 secondi e lasciare il conto alla rovescia a zero per mezzo minuto.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    public void RunOnce()
    {
        int expired = dispatcher.ExpirePendingRequests();
        if (expired > 0)
        {
            // Solo il numero: chi ha bussato non finisce nei log (07 §9, regola 4).
            logger.LogDebug("Richieste di ingresso scadute: {Expired}", expired);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(SweepInterval, timeProvider, stoppingToken);
                RunOnce();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
