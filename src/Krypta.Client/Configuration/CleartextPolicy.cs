namespace Krypta.Client.Configuration;

/// <summary>
/// Dove è ammesso il WebSocket in chiaro. La regola sta in un posto solo perché la
/// applicano in tre: il campo della schermata [1], il salvataggio del config e
/// <c>KryptaConnection</c> prima di aprire il socket. Tre copie che divergono
/// darebbero una spunta che la UI accetta e il trasporto rifiuta.
/// </summary>
public static class CleartextPolicy
{
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>
    /// Solo <c>127.0.0.1</c> esatto: né <c>localhost</c>, che passa da una risoluzione di
    /// nome, né altri indirizzi di loopback ([05 §5bis](../../../docs/05-client-desktop.md)).
    /// </summary>
    public static bool AllowsCleartext(string? host) =>
        string.Equals(host, LoopbackHost, StringComparison.Ordinal);
}
