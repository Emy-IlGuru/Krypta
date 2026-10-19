namespace Krypta.Server.RateLimiting;

/// <summary>
/// Finestra scorrevole: al massimo <paramref name="limit"/> eventi in
/// <paramref name="window"/>. Tiene i soli istanti ancora dentro la finestra, quindi
/// occupa al più <paramref name="limit"/> voci e non cresce nel tempo.
/// </summary>
internal sealed class SlidingWindow(int limit, TimeSpan window)
{
    private readonly Queue<DateTimeOffset> _events = new(limit);

    public bool TryConsume(DateTimeOffset now, out int retryAfterSeconds)
    {
        lock (_events)
        {
            Trim(now);

            if (_events.Count < limit)
            {
                _events.Enqueue(now);
                retryAfterSeconds = 0;
                return true;
            }

            // Il primo evento ancora in finestra è quello che, scadendo, libera il posto.
            TimeSpan remaining = window - (now - _events.Peek());
            retryAfterSeconds = ToWholeSeconds(remaining);
            return false;
        }
    }

    private void Trim(DateTimeOffset now)
    {
        while (_events.Count > 0 && now - _events.Peek() >= window)
        {
            _events.Dequeue();
        }
    }

    /// <summary>
    /// Arrotonda per eccesso: un'attesa di 0 secondi direbbe al client di riprovare
    /// subito, e riprovando otterrebbe di nuovo lo stesso errore.
    /// </summary>
    internal static int ToWholeSeconds(TimeSpan value) =>
        value <= TimeSpan.Zero ? 1 : (int)Math.Ceiling(value.TotalSeconds);
}
