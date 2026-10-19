using System.Collections.Concurrent;
using Krypta.Shared;

namespace Krypta.Server.RateLimiting;

/// <summary>
/// Difende dall'enumerazione degli ID stanza (04 §4.6, 07 §4).
/// Chiave = indirizzo remoto, 5 tentativi ogni 180 s, blocco di 120 s.
/// </summary>
public sealed class JoinRateLimiter(TimeProvider timeProvider)
{
    private static readonly TimeSpan Window =
        TimeSpan.FromSeconds(KryptaLimits.JoinRateLimitWindowSeconds);

    private static readonly TimeSpan Block =
        TimeSpan.FromSeconds(KryptaLimits.JoinRateLimitBlockSeconds);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public int TrackedAddresses => _entries.Count;

    /// <summary>
    /// Da chiamare a <b>ogni</b> messaggio <c>join</c>, non una volta per connessione:
    /// una connessione può fare <c>leave</c> e <c>join</c> all'infinito, quindi contare
    /// solo il primo lascerebbe scandire l'intero spazio degli ID con un socket solo.
    /// </summary>
    public bool TryJoin(string? remoteAddress, out int retryAfterSeconds)
    {
        // Senza indirizzo non c'è niente da limitare. Capita solo nei test in memoria:
        // una connessione HTTP reale ha sempre un indirizzo remoto.
        if (string.IsNullOrEmpty(remoteAddress))
        {
            retryAfterSeconds = 0;
            return true;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Entry entry = _entries.GetOrAdd(remoteAddress, _ => new Entry());

        lock (entry)
        {
            if (entry.BlockedUntil > now)
            {
                retryAfterSeconds = SlidingWindow.ToWholeSeconds(entry.BlockedUntil - now);
                return false;
            }

            entry.Trim(now);

            // Contano TUTTI i tentativi, riusciti e falliti: contare solo i falliti
            // renderebbe l'enumerazione gratuita, perché chi scansiona riceve
            // per lo più risposte valide, non errori.
            if (entry.Attempts.Count >= KryptaLimits.JoinRateLimitAttempts)
            {
                entry.BlockedUntil = now + Block;
                entry.Attempts.Clear();
                retryAfterSeconds = SlidingWindow.ToWholeSeconds(Block);
                return false;
            }

            entry.Attempts.Enqueue(now);
            entry.LastActivity = now;
            retryAfterSeconds = 0;
            return true;
        }
    }

    /// <summary>
    /// Rimuove le voci inattive. Senza, il dizionario cresce quanto il numero di indirizzi
    /// che hanno mai provato a entrare: è una piccola perdita di memoria, ed è pilotabile
    /// da fuori. Viene chiamata dall'<c>HeartbeatService</c>.
    /// </summary>
    public int Prune()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        int removed = 0;

        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            Entry entry = pair.Value;
            bool expired;

            lock (entry)
            {
                entry.Trim(now);
                expired = entry.Attempts.Count == 0
                    && entry.BlockedUntil <= now
                    && now - entry.LastActivity >= Window;
            }

            if (expired && _entries.TryRemove(pair))
            {
                removed++;
            }
        }

        return removed;
    }

    private sealed class Entry
    {
        public Queue<DateTimeOffset> Attempts { get; } = new();

        public DateTimeOffset BlockedUntil { get; set; }

        public DateTimeOffset LastActivity { get; set; }

        public void Trim(DateTimeOffset now)
        {
            while (Attempts.Count > 0 && now - Attempts.Peek() >= Window)
            {
                Attempts.Dequeue();
            }
        }
    }
}
